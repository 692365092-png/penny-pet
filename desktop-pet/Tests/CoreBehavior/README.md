# Core behavior tests

The domain files are partial declarations of the existing `CoreBehaviorTests`
MSTest class. `../CoreBehaviorTests.cs` supplies its single `[TestClass]`
declaration. Test method names, data rows, assertions, fixture helpers and fully
qualified test identities are preserved; only source-file ownership changed.

- Settings, Sticky models/import/codec/backup/placement and Dock cover persisted
  data and pure rules.
- Display and SideTabs cover detached geometry and placement rules.
- Reminders, keyboard, interaction and art cover scheduling/input behavior.
- Daily content, solar terms, almanac and weather cover content selection and copy.

Private helpers live beside their domain and remain available through the same
partial class. The existing project glob includes every file; no custom test
framework, reflection discovery or new runner is introduced.
