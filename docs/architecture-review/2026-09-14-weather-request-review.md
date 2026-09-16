# Weather request ownership — 2026-09-14

## Problem

`PetWeatherSource` mixed HTTP transport, successful-value caching, a single
in-flight request slot, and a single failure-cooldown slot. Calling A → B → A
while both cities were loading could start A twice. A successful request for B
also cleared A's failure cooldown. `Task.Delay(1)` delayed execution to make task
registration happen before completion; it encoded an ordering requirement as a
timer.

## Change

`WeatherForecastCache` owns the completed cache and in-flight requests by the
existing stable location key. It registers a completion task before invoking the
provider, so synchronous and asynchronous providers use the same ordering. An
independent city's completion cannot retire another city's request.

The completed cache retains at most three entries, including failures. A retained
failure has its own 15-minute cooldown. The city-local day still determines
successful-value expiry. Invalidating completed entries preserves active requests,
as before. A provider exception completes the shared task with that exception and
retires the registration, allowing a later retry.

`PetWeatherSource` owns HTTP clients, the 3-second forecast deadline, the separate
8-second city-search deadline, disposal, diagnostics, and the existing quiet
forecast fallback. Expected transport failures still become `null` before the
cache receives them. No HTTP or Windows dependency was added to Core, and no new
package or background worker was added.

The structural guard now checks the transport-to-cache boundary rather than
requiring the old single-slot field names. Actual behavior is covered by seven
tests: alternating cities, synchronous completion, per-city failure cooldown,
city-local midnight, three-entry eviction, provider failure and retry, and
invalidation during a request.

## Validation

- Core and test sources compiled with warnings treated as errors.
- 524 MSTest method/data-row invocations passed, zero failed, using the direct
  runner described in the earlier reviews. This is not a successful VSTest run.
- The actual Windows Core and SelfTests project source sets compiled as separate
  assemblies against .NET Framework 4.8 reference assemblies.
- No live weather API or Windows UI test was run in this Linux environment.

This fixes request duplication and ordering; it does not claim lower remote API
latency or a measured change in GUI frame rate.
