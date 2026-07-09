# Booking Concurrency

## Status

Concurrent double-booking prevention is implemented.

The booking flow now uses:

```text
SQL Server retry execution strategy
  -> begin Serializable transaction
  -> find one available room
  -> insert booking
  -> commit
```

A SQL Server integration test forces two requests to observe the final
available room concurrently. Exactly one request succeeds, the other returns
`409 Conflict`, and the database contains no overlapping booking.

## The Bug

The original implementation checked availability inside a transaction, but
used SQL Server's default `ReadCommitted` isolation:

```text
Request A                         Request B
---------                         ---------
begin transaction                begin transaction
room 202 appears available       room 202 appears available
select room 202                  select room 202
insert booking A                 insert booking B
commit                           commit
```

Both requests could commit because a normal unique index cannot express
"date ranges for this room must not overlap."

The overlap predicate was correct:

```csharp
existing.CheckInDate < requested.CheckOutDate
    && requested.CheckInDate < existing.CheckOutDate
```

The missing part was transaction isolation. Correct logic in two concurrent
requests is not enough unless the database protects the observed range until
the write commits.

## Why Checking Twice Does Not Fix It

This sequence still has a race under `ReadCommitted`:

```text
check availability
start transaction
check availability again
save
```

Two requests can perform the second check before either inserts. Repeating the
query narrows the timing window but does not close it.

Correctness comes from the transaction's isolation and locking behavior, not
the number of checks.

## Implemented Fix

### Serializable Transaction

`BookingService` starts the transaction with:

```csharp
await dbContext.Database.BeginTransactionAsync(
    IsolationLevel.Serializable,
    cancellationToken);
```

`Serializable` prevents phantom rows for the protected query range. SQL Server
uses key-range locking where supported by the query access path, so another
transaction cannot insert a conflicting booking as though the checked range
were still empty.

The transaction is deliberately short:

1. Find candidate rooms by hotel, room type, and capacity.
2. Exclude rooms with overlapping bookings.
3. Select one room deterministically.
4. Generate a booking reference.
5. Insert the booking.
6. Commit.

No HTTP calls, user interaction, email, or unrelated work occurs inside the
transaction.

### SQL Server Retry Strategy

Serializable transactions may deadlock when two requests initially acquire
compatible read locks and then both attempt conflicting writes. SQL Server
selects one transaction as the deadlock victim.

The repository configures EF Core's SQL Server retry strategy:

```csharp
sqlServerOptions.EnableRetryOnFailure(
    maxRetryCount: 5,
    maxRetryDelay: TimeSpan.FromSeconds(2),
    errorNumbersToAdd: null);
```

The complete transaction runs inside `CreateExecutionStrategy().ExecuteAsync`,
so a transient SQL failure replays the whole unit rather than retrying only one
command.

At the beginning of each attempt:

```csharp
dbContext.ChangeTracker.Clear();
```

This prevents entities left in a failed attempt's tracking state from leaking
into the retry.

### Deterministic Room Order

Available rooms are ordered by:

1. Capacity.
2. Room type.
3. Room number.

Requests therefore consider rooms in a stable order. This keeps behavior
predictable and reduces inconsistent lock acquisition order.

## Concurrency Integration Test

The test uses disposable SQL Server through Testcontainers, not EF InMemory.

Test sequence:

1. Seed two double rooms.
2. Book the first room.
3. Arm a test-only EF command interceptor.
4. Start two booking requests for the same dates.
5. Pause both requests after their availability queries complete.
6. Release both requests together.
7. Assert one returns `201 Created`.
8. Assert one returns `409 Conflict`.
9. Query the database and confirm two bookings use two distinct room IDs.

The barrier makes the race deterministic. A test that merely starts two tasks
could pass because one request happened to finish before the other began its
availability query.

The test proves the behavior through the real API, EF Core, migrations,
transactions, retry strategy, and SQL Server locking.

## Resulting API Behavior

When two requests compete for the final suitable room:

```text
Winner: 201 Created
Loser:  409 Conflict
```

The conflict response uses `ProblemDetails`:

```json
{
  "title": "Conflict",
  "status": 409,
  "detail": "No room is available for the requested stay and guest count."
}
```

This is a normal business conflict, not an internal server error.

## Index Considerations

Serializable isolation is most efficient when SQL Server can protect a focused
index range instead of taking broad locks.

The booking overlap query uses:

```text
HotelId
RoomId
CheckInDate
CheckOutDate
```

The current model has an index over those fields. For substantially higher
traffic, inspect the generated SQL and Azure SQL execution plan to confirm that
the optimizer uses an appropriate seek or focused range scan.

Index order should be changed only from measured query-plan evidence.

## Remaining Future Work

The double-booking bug is fixed. The following are separate production
improvements, not requirements for enforcing the challenge rule.

### Idempotency Key

If a connection drops while commit is in progress, the caller may not know
whether the booking succeeded. A production API can accept:

```text
Idempotency-Key: client-generated-unique-value
```

Store it with a unique index. Repeated client requests can then return the
original booking instead of creating another.

### Booking Reference Collision

The unique booking-reference index protects the database, but a rare collision
between concurrent generators could still surface as a failed insert. A future
change can catch that specific unique-key violation and regenerate the
reference inside the retry boundary.

### Higher-Scale Locking Strategy

Serializable transactions can reduce throughput because conflicting requests
block or retry. If measured booking volume becomes high:

1. Inspect lock duration and deadlock rates.
2. Review the overlap-query index.
3. Consider partitioning contention by hotel or room.
4. Compare with a SQL Server application lock.

## Alternative: SQL Application Lock

SQL Server's `sp_getapplock` could serialize booking creation per hotel:

```text
begin transaction
acquire exclusive lock "hotel-booking:{hotelId}"
find room
insert booking
commit
```

This has a simple correctness model but:

- Serializes bookings that might not otherwise conflict.
- Couples the service directly to SQL Server.
- Requires lock timeout and error handling.

The implemented serializable transaction keeps the current EF query model and
allows SQL Server to manage the protected ranges.

## Approaches Not Used

### In-Memory Lock

`lock` or `SemaphoreSlim` protects one API process only. Azure Container Apps
can run multiple replicas.

### Optimistic Concurrency Token

A row-version token protects updates to an existing row. An overlap conflict
can be an insert into a previously empty date range, where no row exists to
carry a version.

### Booking Reference Unique Index

This protects reference uniqueness, not room/date overlap.

### Distributed Cache Lock

This adds another infrastructure dependency and failure mode. The SQL database
already owns booking consistency.

## Verification

The fix is complete because:

- Booking uses a `Serializable` transaction.
- SQL Server transient retries are bounded.
- Retry attempts start with clean EF tracking state.
- Concurrent requests cannot create overlapping bookings.
- The losing request returns `409 ProblemDetails`.
- Integration tests run against disposable SQL Server.
- Back-to-back date semantics remain unchanged.

## References

- [SQL Server transaction locking and row versioning guide](https://learn.microsoft.com/en-us/sql/relational-databases/sql-server-transaction-locking-and-row-versioning-guide)
- [SQL Server transaction isolation levels](https://learn.microsoft.com/en-us/sql/t-sql/statements/set-transaction-isolation-level-transact-sql)
- [EF Core connection resiliency and transaction retries](https://learn.microsoft.com/en-us/ef/core/miscellaneous/connection-resiliency)
- [EF Core SQL Server provider](https://learn.microsoft.com/en-us/ef/core/providers/sql-server/)

