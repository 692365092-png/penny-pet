# Structural guards

18 discoverable cases protect dependency and safety boundaries. These guards
may reject forbidden dependencies, harness code in product builds, synchronous
UI cross-thread waits, automation in keyboard hooks, or network calls at startup.
They do not prescribe private method names, statement order, or protocol factory
spelling. Core is checked both as source and as a compiled assembly; product
compile exclusions and build-only Tools references are checked from project XML.

The former 111 source-guard cases were replaced by these boundary checks. The
additional source-spelling test embedded in CoreBehaviorTests was also removed.
Its coordinate behavior remains covered by placement round-trip and mixed-DPI
tests. Existing behavioral and native probes were retained without changes:

| Removed source checks | Runtime coverage |
| --- | --- |
| Dock commit, stale facts, topology, group restore, rollback | Tests/Dock* and Tests/StickyFactsReceiverTests; SelfTests/DockLocalRuntimeChecks and DockDragIntegrationChecks |
| Settings/notes persistence, future schema, compatibility | Tests/AsyncPersistenceBarrierTests, PetPersistenceRuntimeTests, StickyCompatibilityMigrationTests; native persistence and compatibility probes |
| Keyboard privacy and partial hook failure | Tests/KeyboardPrivacyWorkerTests; SelfTests/KeyboardPrivacyChecks |
| Sticky content, editor, reminder, IME and host lifetime | SelfTests/StickyContentViewChecks, StickyReminderHostChecks, PersistencePauseChecks and hosted lifecycle probes |
| Art loading, conversation, reminders, daily content, weather | Core behavior tests; Tests/WeatherForecastCacheTests; SelfTests/ArtAssetChecks, InteractionRuntimeChecks, ConversationRuntimeChecks and ReminderRuntimeChecks |
| Pet/SideTab placement and user preferences | Tests/PetDisplayRuntimeTests, StartupPetPlacementTests, SideTabProjectionTests and mixed-DPI/native placement probes |

The table identifies coverage owners, not a claim that every deleted string
assertion has an identical runtime assertion. UI menu labels and historical
implementation layouts are deliberately not hard architecture contracts.
