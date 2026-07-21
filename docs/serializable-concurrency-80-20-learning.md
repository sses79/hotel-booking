# Serializable Transactions and Concurrency Tests: The 80/20 Guide

This guide explains the small number of ideas that provide most of the value
when reasoning about `IsolationLevel.Serializable` and the test
`Concurrent_requests_cannot_double_book_the_last_available_room`.

The aim is not to memorize every SQL Server lock type. The aim is to develop a
reusable way to protect business rules when multiple requests run at the same
time.

## The 80/20 Summary

Learn these five ideas first:

1. **Start with an invariant:** one room must not have overlapping bookings.
2. **A correct query is not enough:** two requests can both read "available"
   before either one writes.
3. **Put the decision and write in one transaction:** availability must be read
   after the serializable transaction begins.
4. **Serializable protects the predicate:** SQL Server prevents another
   transaction from changing the protected result as if the checked range were
   still empty.
5. **A good concurrency test controls timing:** deliberately place both requests
   at the dangerous point instead of hoping a race occurs.

These principles explain most of the implementation. Lock names, execution
plans, retry error numbers, and performance tuning are useful second-stage
topics.

## 1. Begin With The Business Invariant

An invariant is a rule that must remain true regardless of how many requests
arrive together:

> A room cannot have two bookings that occupy the same night.

Bookings use half-open ranges:

```text
[checkInDate, checkOutDate)
```

Two ranges overlap when:

```csharp
existing.CheckInDate < requested.CheckOutDate
    && requested.CheckInDate < existing.CheckOutDate
```

This predicate is correct for one query, but preserving the invariant requires
more than a correct predicate.

### Reusable lesson

Whenever code follows this shape, look for a concurrency risk:

```text
read current state
make a decision
write new state
```

Examples include reserving stock, spending an account balance, claiming a
coupon, allocating a seat, or creating a username. Ask:

> What if two requests read the same state before either writes?

## 2. Understand The Race Before The Fix

Imagine only room 202 remains available:

```text
Request A                         Request B
---------                         ---------
queries availability              queries availability
sees room 202                     sees room 202
chooses room 202                  chooses room 202
inserts booking A                 inserts booking B
commits                           commits
```

Each request is locally correct, but the combined result violates the business
rule.

This is a **check-then-act race**. The problem is the gap between checking the
state and changing it.

### Why common quick fixes are insufficient

Checking twice does not close the race:

```text
check availability
begin transaction
check availability again
insert
```

Both requests can still perform the second check before either inserts.

An in-memory `lock` or `SemaphoreSlim` protects only one API process. The Azure
deployment can run multiple replicas, so another process could bypass it.

A normal unique index cannot express arbitrary overlapping date ranges. These
two bookings have different dates but overlap:

```text
[1 August, 3 August)
[2 August, 4 August)
```

## 3. Put The Read And Write In One Transaction

The important transaction boundary in `BookingService` is:

```csharp
await using var transaction =
    await dbContext.Database.BeginTransactionAsync(
        IsolationLevel.Serializable,
        cancellationToken);

var room = await FindFirstAvailableRoomAsync(
    command,
    cancellationToken);

// Create and add the booking.
await dbContext.SaveChangesAsync(cancellationToken);
await transaction.CommitAsync(cancellationToken);
```

The sequence is:

```text
BEGIN SERIALIZABLE TRANSACTION
    read availability
    choose a room
    insert the booking
COMMIT
```

The availability query must be after `BeginTransactionAsync`. Starting a
transaction later cannot retroactively protect a read that already occurred.

### Atomic decision

Think of the transaction as protecting one decision:

> Based on the availability I observed, I am assigning this room.

The observation and assignment belong in the same consistency boundary.

### Keep the transaction short

Only required database work should occur inside it:

- query availability;
- select the room;
- generate the reference;
- insert the booking; and
- commit.

Do not put email, HTTP calls, user interaction, or slow unrelated work inside a
serializable transaction. Longer transactions retain locks longer and increase
blocking and deadlock risk.

## 4. What Serializable Means In Practice

The simplest useful mental model is:

> While my transaction is active, another transaction cannot make the rows
> matching my protected query appear, disappear, or change in a way that would
> invalidate my decision.

For availability, SQL Server must protect not only existing booking rows but
also relevant **gaps** where a conflicting booking could be inserted. This is
why serializable isolation is associated with key-range locking.

### It does not simply lock "the first room"

The current availability service calls `ToListAsync`, so the database query
loads all suitable available rooms before the application orders them and
chooses the first one. The exact locks depend on:

- the generated SQL;
- available indexes;
- the SQL Server execution plan; and
- the rows and key ranges scanned.

Locks may therefore cover more than the room eventually selected. A poor query
plan can also cause broader locking than expected.

The correct interview statement is:

> Serializable protects the availability query's relevant rows and key ranges
> until commit or rollback; the exact lock footprint depends on the execution
> plan and indexes.

### What competing requests may experience

Another transaction may:

- wait for locks;
- be selected as a deadlock victim;
- retry and observe the newly committed booking; or
- continue if it does not conflict with the protected range.

Serializable does not mean all database work becomes single-threaded. It means
the database preserves behavior equivalent to transactions running one after
another for the protected operations.

### When locks are released

Locks held by the transaction are released when it commits or rolls back:

```text
BEGIN -> acquire/retain locks -> read -> write -> COMMIT -> release locks
```

If an attempt fails, disposal rolls back that transaction and releases its
locks. A retry starts a new transaction and acquires new locks. Locks are not
held continuously between attempts.

## 5. Retry The Whole Transaction

Serializable transactions can deadlock. For example, two requests may both
read compatible state and then both attempt conflicting writes. SQL Server
breaks the deadlock by failing one transaction.

The project configures the SQL Server execution strategy with:

```csharp
EnableRetryOnFailure(
    maxRetryCount: 5,
    maxRetryDelay: TimeSpan.FromSeconds(2),
    errorNumbersToAdd: null)
```

`BookingService` runs the complete transaction inside:

```csharp
var strategy = dbContext.Database.CreateExecutionStrategy();

var result = await strategy.ExecuteAsync(async () =>
{
    // Begin transaction, read availability, insert, commit.
});
```

The entire decision must be replayed because database state may have changed:

```text
Attempt 1
    begin transaction
    see room 202
    lose deadlock
    roll back and release locks

Retry
    clear tracked EF state
    begin a new transaction
    query again
    now see room 202 is occupied
    return no availability
```

`maxRetryCount: 5` means the initial attempt plus up to five retries. It does not
mean that every exception is retried. The provider retries errors it classifies
as transient. Invalid requests, cancellation, ordinary business conflicts, and
most programming errors are not retry cases.

### Why clear the change tracker?

```csharp
dbContext.ChangeTracker.Clear();
```

A failed attempt may leave entities in EF Core's tracked state. Clearing the
tracker makes each replay start from current database state instead of carrying
stale in-memory state from the failed attempt.

### Reusable lesson

Never retry only the failed insert after a transactional decision. Retry from
the read that produced the decision.

## 6. Why The Concurrent Integration Test Exists

The test is named:

```csharp
Concurrent_requests_cannot_double_book_the_last_available_room
```

Its job is not merely to send two requests. Its job is to prove the invariant
under a deliberately dangerous interleaving.

### Test setup

The seeded hotel contains two double rooms: 201 and 202.

The test first creates one booking:

```csharp
using var firstBooking = await CreateBookingAsync(
    client,
    "First guest",
    checkIn,
    checkOut);
```

Deterministic room ordering assigns room 201, leaving room 202 as the final
double room.

The test then arms the barrier and starts two HTTP requests:

```csharp
barrier.Arm();

var concurrentResponses = await Task.WhenAll(
    CreateBookingAsync(client, "Second guest", checkIn, checkOut),
    CreateBookingAsync(client, "Third guest", checkIn, checkOut));
```

`Task.WhenAll` waits until both requests finish. It does not itself guarantee
that they query availability at the same instant. That guarantee is the
barrier's job.

## 7. How The Test Barrier Works

`AvailabilityBarrierInterceptor` is a test-only EF Core command interceptor.
It watches executed SQL until it recognizes the room availability query:

```csharp
return commandText.Contains("FROM [Rooms] AS [r]")
    && commandText.Contains("NOT EXISTS");
```

After the first matching query completes:

```text
counter becomes 1
request awaits the barrier signal
```

After the second matching query completes:

```text
counter becomes 2
the barrier signal is completed
both requests continue
```

The core code is:

```csharp
if (Interlocked.Increment(ref _completedQueryCount) == 2)
{
    _armed = false;
    _bothQueriesCompleted.TrySetResult();
}

await _bothQueriesCompleted.Task.WaitAsync(
    TimeSpan.FromSeconds(10),
    cancellationToken);
```

### The async primitives in plain language

`TaskCompletionSource` is a manually controlled, one-time signal:

```text
Task                      = the side requests await
TrySetResult()            = the side that releases the requests
```

`Interlocked.Increment` safely counts arrivals when two threads may update the
counter concurrently.

`RunContinuationsAsynchronously` schedules released continuations instead of
running them immediately inside `TrySetResult`. This reduces re-entrancy and
surprising nested execution inside the interceptor.

`WaitAsync` adds a ten-second timeout so a broken query match fails the test
instead of hanging indefinitely.

### Complete test timeline

```text
Seed two double rooms
        |
Book first room (201)
        |
Arm interceptor
        |
Start request A and request B
        |
        +---------------------------+
        |                           |
A executes availability SQL   B executes availability SQL
        |                           |
barrier count = 1             barrier count = 2
A waits                       B completes signal
        |                           |
        +----- both released -------+
                     |
          both compete for room 202
                     |
       serializable transaction decides outcome
                     |
           one 201 and one 409
```

The interceptor does not protect production bookings. It only creates the
timing needed to test the real protection supplied by SQL Server.

## 8. What The Assertions Prove

The test verifies one success and one conflict:

```csharp
Assert.Equal(
    1,
    concurrentResponses.Count(response =>
        response.StatusCode == HttpStatusCode.Created));

Assert.Equal(
    1,
    concurrentResponses.Count(response =>
        response.StatusCode == HttpStatusCode.Conflict));
```

It then queries persisted bookings and verifies:

```csharp
Assert.Equal(2, bookings.Count);
Assert.Equal(
    2,
    bookings.Select(booking => booking.RoomId).Distinct().Count());
```

Together these prove:

- the original booking still exists;
- exactly one competing request created another booking;
- the losing request received the expected business response; and
- the two persisted bookings use different rooms.

Checking only HTTP status codes would be weaker because it would not directly
verify the final database invariant.

The test uses a real disposable SQL Server because EF InMemory cannot prove SQL
Server locking, isolation, deadlock, execution-strategy, or migration behavior.

## 9. What This Test Does Not Prove

Good engineering includes understanding the boundary of the evidence.

This test does not prove:

- performance under thousands of simultaneous bookings;
- fairness—which request should win;
- that the query uses the best possible index;
- correct behavior for every SQL Server execution plan;
- idempotency when a client retries after losing a response;
- recovery from every possible failure around commit; or
- booking-reference collision recovery.

The barrier is also specialized for two requests and is one-shot. If three
requests must be synchronized, it should accept an expected participant count
of three and a fresh interceptor should be created for that test.

Its SQL recognition uses generated SQL text, so an EF Core translation change
could require updating the test. The timeout makes that coupling fail visibly.

## 10. Common Misunderstandings

### “Serializable locks the returned room.”

Not exactly. It protects rows and key ranges used by the query. The exact
footprint depends on indexes and the execution plan, and may be broader than the
eventually selected room.

### “The locks stay held during retries.”

No. A failed attempt rolls back and releases its locks. The retry creates a new
transaction and reacquires locks after rerunning the query.

### “Five retries means five total attempts.”

No. `maxRetryCount: 5` permits the initial attempt plus up to five retries.

### “`Task.WhenAll` forces a race.”

No. It starts/observes concurrent asynchronous operations and waits for all of
them. Scheduling is nondeterministic. The interceptor barrier forces both
requests to reach the chosen point before either continues.

### “`TaskCompletionSource` creates a background thread.”

No. It creates a task whose completion is controlled by code. It is a signal,
not a worker.

### “The concurrency interceptor fixes double booking.”

No. It exists only in integration tests. The serializable database transaction
is the production protection.

### “A unique booking-reference index prevents room overlap.”

No. It prevents two bookings from sharing a reference. Room/date overlap is a
different invariant requiring separate protection.

## 11. Interview-Ready Explanations

### 30-second version

> Availability is a check-then-insert operation, so two requests can both see
> the same final room as free. I place the availability query and booking insert
> inside one short serializable SQL Server transaction. That protects the
> relevant query range until commit. Because serializable contention can cause
> transient failures or deadlocks, EF retries the whole transaction and reruns
> availability against current state. The integration test uses a test-only EF
> interceptor to pause two requests after both availability queries, forcing the
> race; it then proves one succeeds, one gets 409, and no room is duplicated.

### One-sentence principle

> Protect the decision, not only the write, and test the dangerous interleaving
> rather than hoping it happens.

### If asked why not use an application lock

> An in-process lock would protect only one API replica. SQL Server is the
> shared consistency boundary for all replicas. A SQL application lock could be
> an alternative, but it increases provider coupling and may serialize more
> work than necessary.

### If asked about the cost

> Serializable favors correctness and can increase blocking or deadlocks under
> contention. I keep the transaction short, use a suitable index and bounded
> retry, then would measure query plans, lock duration, and retry rates before
> adopting a more complex allocation strategy.

## 12. Turn This Development Into A Reusable Practice

For every future feature that reads before writing, use this checklist.

### Business rule

- What must always remain true?
- Is the rule about one row, several rows, or the absence of a row?

### Race analysis

- What happens if two requests read the same state?
- Can both decisions be valid individually but invalid together?
- Can the database express the rule with a constraint?

### Transaction design

- Does the transaction begin before the decision-making read?
- Is the write inside the same transaction?
- Is the transaction as short as possible?
- What isolation or concurrency mechanism protects the invariant?

### Failure design

- Which errors are genuinely transient?
- Does a retry rerun the whole decision?
- Is retry bounded?
- Could an unknown commit result require an idempotency key?

### Test design

- Can the unsafe interleaving be forced deterministically?
- Does the test use the real database behavior that matters?
- Does it assert both the API result and final persisted state?
- Does the test have a timeout instead of being able to hang forever?

### Operational design

- What contention, deadlocks, retries, and latency should be monitored?
- Does the relevant index support a focused lock range?
- At what measured load would a different strategy become worthwhile?

## 13. Small Learning Experiments

These experiments build understanding without changing the production design.
Run each on a temporary branch and restore the correct implementation afterward.

### Experiment 1: Move availability outside the transaction

Temporarily run `FindFirstAvailableRoomAsync` before
`BeginTransactionAsync`. Predict the test result, then run the concurrency test.
This demonstrates why transaction placement matters more than merely having a
transaction somewhere in the method.

### Experiment 2: Remove the barrier

Run the two requests with `Task.WhenAll` but without the interceptor. Repeat the
test many times. It may still pass, showing why a passing nondeterministic
concurrency test is weak evidence.

### Experiment 3: Log generated SQL

Use `ToQueryString()` on the availability query or enable EF SQL logging in a
test environment. Find the correlated `NOT EXISTS` and connect the LINQ
predicate to the SQL protected by the transaction.

### Experiment 4: Inspect the actual execution plan

Run the generated SQL in SQL Server with an actual execution plan. Identify the
room and booking index access. This turns the abstract phrase "range lock" into
a concrete understanding of what keys SQL Server scans.

### Experiment 5: Generalize to three requests and two rooms

Change the barrier to accept an expected arrival count of three. Start three
requests with two rooms free and assert two `201 Created` responses, one
`409 Conflict`, and two distinct room IDs. This separates production
concurrency behavior from the current two-participant test helper.

### Experiment 6: Observe retry attempts

Add temporary structured logging around the execution-strategy callback. Run
the concurrency test and observe that a retry begins a new transaction and
reruns availability rather than continuing from the failed write.

## Final Mental Model

Remember this sequence:

```text
Invariant
    -> identify check-then-act race
    -> begin transaction before the check
    -> protect the predicate with suitable isolation
    -> perform the write
    -> commit
    -> retry the complete decision when a transient failure occurs
    -> force the dangerous timing in an integration test
    -> assert the final database invariant
```

That model applies far beyond hotel bookings. It is the useful 20 percent that
solves a large share of real concurrency problems.
