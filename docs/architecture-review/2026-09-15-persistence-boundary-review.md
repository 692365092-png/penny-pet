# Persistence boundary simplification — 2026-09-15

`StickyNoteCodec.ParseLine` used eleven version booleans and repeated lists of
supported versions at each appended field. The reader now validates the exact
version token and that version's minimum field count once, then reads additive
fields by their introduction version. The special v1 text layout, v1–v11
compatibility, tolerance of trailing fields, and rejection of unknown, padded,
signed or truncated version/layout input are preserved. No file-format revision
or data migration was introduced.

`StickyNoteRepository.Remove` and `ReorderHidden` now enqueue their detached
snapshots through the existing writer. Neither operation used the synchronous
save receipt previously. Their UI callers can finish while disk writes are
pending; the existing dirty state, failure notification, retry and exit flush
remain responsible for durability. A successful `Remove` means the in-memory
removal succeeded, not that disk persistence has completed. Restart tests now
explicitly wait for that persistence boundary before reloading.

Existing golden fixtures still exercise all eleven versions. Three additional
codec tests cover every truncated fixture prefix, ignored trailing fields and
unrecognized version tokens. Two blocked-writer cases verify deletion and tab
reordering return before the writer is released and that both captured snapshots
remain correct. Source guards no longer insist on obsolete local variable names;
the actual native-executor ownership checks remain intact.

Validation: 529 direct MSTest method/DataRow calls passed, zero failed. Core and
test sources compiled with warnings treated as errors. Windows Core (90 source
files) and SelfTests (5 source files) compiled as separate .NET Framework 4.8
assemblies. This is not native GUI execution or a successful VSTest run; the
environment limitation is detailed in the full review.

The earlier four checkpoints were recovered from the saved cumulative patch in
an isolated worktree. All four original commit hashes were reproduced exactly;
the interrupted workspace was preserved. The remote branch was still at
`f5bd7859654b955b2cf21978d660ca4c21bfd114` when checked on 2026-09-15.
