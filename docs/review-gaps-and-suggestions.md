# Review: Gaps and Suggestions

A review of the hotel booking solution against the challenge brief — code,
tests, docs, and infrastructure. The structure is genuinely good (clean
layering, `TimeProvider` injection, ProblemDetails responses, Testcontainers
integration tests, deterministic seed data). The original significant
concurrency gap is now resolved; a few smaller improvements remain.

## Resolved: The No-Double-Booking Rule

The original review correctly identified that a `ReadCommitted` transaction
did not protect the availability check from concurrent inserts.

The implementation now:

- Uses `IsolationLevel.Serializable`.
- Enables bounded SQL Server retries.
- Clears failed EF tracking state before a retry.
- Runs the complete transaction inside the execution strategy.
- Includes a deterministic SQL Server integration test where two requests
  compete for the final room.

The test asserts exactly one `201 Created`, one `409 Conflict`, and no
overlapping room assignment. See `docs/booking-concurrency.md`.

## Remaining End-To-End Coverage Gaps

The concurrent overlapping-booking path is now tested end to end. There is
still no API-level test that a party of 5 cannot get a room, or that
back-to-back bookings (checkout day = check-in day) succeed.

Those rules are only tested as pure functions in `BookingRulesTests`, which
proves the predicate is right, not that the API wires it up. For a booking
API, the "double booking is rejected" test is the single most valuable test in
the suite, and it is missing.

## Smaller Gaps, in Priority Order

1. **Booking reference generation has its own race with an ugly failure
   mode.** `GenerateBookingReferenceAsync` does check-then-insert; the unique
   index backstops it, but a collision at insert time throws an unhandled
   `DbUpdateException` → raw 500. There is also no global exception handler in
   `Program.cs` (no `AddProblemDetails()` + `UseExceptionHandler()`), so any
   unhandled error breaks the otherwise-consistent ProblemDetails contract.
   Catch the unique violation and retry, and add the exception handler
   middleware.

2. **Validation lives in two places with inconsistent behavior.**
   `HotelsController.FindAvailableRooms` validates dates/guests and returns
   400, while `RoomAvailabilityService` re-validates and **silently returns an
   empty list**. A silent `[]` for invalid input is indistinguishable from "no
   rooms available" for any other caller. Make the service return a result
   that distinguishes "invalid request" from "no availability," and let
   controllers map it — one source of truth.

3. **Minor polish:**
   - `TestDataService.ResetAsync` loads every row into memory to delete it —
     use `ExecuteDeleteAsync`.
   - Hotel search passes user input straight into `Contains`, so `%` and `_`
     act as unescaped LIKE wildcards.
   - A whitespace-only `GuestName` passes `[MinLength(1)]` and gets stored as
     an empty string after trimming.
   - There is no upper bound on stay length.

   None of these matter much for the challenge, but they are cheap fixes.

## Non-Gap Worth Confirming

`.env` / `.env.azure` contain real-looking SA and Azure SQL passwords but are
correctly git-ignored (only `.env.example` is committed) — keep it that way.

## Bottom Line

The highest-risk gap is resolved. The next useful tests are API-level capacity
and back-to-back booking scenarios, followed by the smaller validation and
error-handling improvements listed above.
