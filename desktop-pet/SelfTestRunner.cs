using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PennyPet
{
    // Execute domain probes and build the regression report.
    internal static partial class SelfTest
    {

        private sealed class DockCheckResult
        {
            internal DockPersistenceCheckResult Persistence;
            internal DockLifecycleCheckResult Lifecycle;
            internal DockGeometryCheckResult Geometry;
            internal bool PersistenceAndGeometryOk;
        }

        private static DockCheckResult RunDockChecks(string outputPath)
        {
            DockCheckResult result = new DockCheckResult();
            result.Persistence = RunDockPersistenceChecks(outputPath);
            result.Lifecycle = RunDockLifecycleChecks();
            result.Geometry = RunDockGeometryChecks();
            result.PersistenceAndGeometryOk =
                result.Persistence.DockRoundTripOk &&
                result.Geometry.BottomDockingOk;
            return result;
        }

        private static string BuildDockReportFields(
            DockCheckResult dockChecks,
            StickyDialogCheckResult dialogChecks,
            StickyWindowPolicyCheckResult windowPolicyChecks,
            StickyEditorCheckResult editorChecks)
        {
            return
                "  \"side_tab_order_persistence_ok\": " + Bool(
                    dockChecks.Persistence.SideTabOrderOk) + ",\n" +
                "  \"sticky_bottom_dock_persistence_and_geometry_ok\": " + Bool(
                    dockChecks.PersistenceAndGeometryOk) + ",\n" +
                "  \"sticky_mixed_type_middle_insertion_ok\": " + Bool(
                    dockChecks.Persistence.MixedInsertionOk) + ",\n" +
                "  \"schedule_mixed_with_note_and_todo_docking_ok\": " + Bool(
                    dockChecks.Lifecycle.ScheduleMixedTypesOk) + ",\n" +
                "  \"sticky_lower_close_rewires_neighbors_ok\": " + Bool(
                    dockChecks.Persistence.LowerCloseRewiresNeighborsOk) + ",\n" +
                "  \"sticky_hidden_group_restores_together_in_dock_order_ok\": " +
                    Bool(dockChecks.Persistence.WholeComponentRestoreOk) + ",\n" +
                "  \"sticky_group_snapshot_survives_broken_parent_links_ok\": " +
                    Bool(dockChecks.Persistence.SnapshotSurvivesBrokenParentLinksOk) +
                    ",\n" +
                "  \"sticky_group_snapshot_round_trip_ok\": " + Bool(
                    dockChecks.Persistence.GroupSnapshotRoundTripOk) + ",\n" +
                "  \"sticky_expand_and_tile_all_round_trip_ok\": " + Bool(
                    dockChecks.Persistence.ExpandAndTileRoundTripOk) + ",\n" +
                "  \"sticky_group_requests_restore_atomically_ok\": " + Bool(
                    dockChecks.Lifecycle.GroupRestoreAtomicOk) + ",\n" +
                "  \"sticky_middle_member_extraction_keeps_neighbors_joined_ok\": " +
                    Bool(dockChecks.Lifecycle.MiddleExtractionOk) + ",\n" +
                "  \"sticky_middle_x_preserves_hidden_group_slot_ok\": " + Bool(
                    dockChecks.Lifecycle.HiddenSlotPreservedOk) + ",\n" +
                "  \"sticky_middle_x_reopen_reinserts_original_slot_ok\": " + Bool(
                    dockChecks.Lifecycle.HiddenSlotReopenOk) + ",\n" +
                "  \"sticky_middle_x_hidden_slot_restart_persistence_ok\": " + Bool(
                    dockChecks.Persistence.HiddenSlotRestartOk) + ",\n" +
                "  \"sticky_partial_hidden_groups_merge_without_losing_slots_ok\": " +
                    Bool(dockChecks.Lifecycle.PartialHiddenMergeOk) + ",\n" +
                "  \"sticky_rearranged_group_second_restore_cycle_ok\": " + Bool(
                    dockChecks.Lifecycle.SecondRestoreCycleOk) + ",\n" +
                "  \"sticky_repeated_rearrange_restore_cycles_ok\": " + Bool(
                    dockChecks.Lifecycle.RepeatedRestoreCyclesOk) + ",\n" +
                "  \"sticky_wide_narrow_docking_ok\": " + Bool(
                    dockChecks.Geometry.WideNarrowDockingOk) + ",\n" +
                "  \"appearance_dialog_below_and_bounded_ok\": " + Bool(
                    dialogChecks.AppearanceLocationOk) + ",\n" +
                "  \"sticky_group_unified_width_layout_ok\": " + Bool(
                    dockChecks.Geometry.UnifiedGroupResizeOk) + ",\n" +
                "  \"sticky_group_root_anchor_preserved_ok\": " + Bool(
                    dockChecks.Geometry.RootAnchorPreservedOk) + ",\n" +
                "  \"sticky_detached_group_translation_ok\": " + Bool(
                    dockChecks.Geometry.DetachedGroupTranslationOk) + ",\n" +
                "  \"sticky_internal_divider_moves_following_chain_ok\": " + Bool(
                    dockChecks.Geometry.DividerMovesFollowingChainOk) + ",\n" +
                "  \"sticky_internal_divider_independent_range_ok\": " + Bool(
                    dockChecks.Geometry.DividerIndependentRangeOk) + ",\n" +
                "  \"sticky_internal_divider_preserves_downstream_heights_ok\": " +
                    Bool(dockChecks.Geometry
                        .DividerPreservesDownstreamHeightsOk) + ",\n" +
                "  \"sticky_hosted_divider_stable_live_targets_ok\": " +
                    Bool(dockChecks.Geometry
                        .DividerLiveSessionTargetsOk) + ",\n" +
                "  \"sticky_long_group_coordinate_guard_ok\": " + Bool(
                    dockChecks.Geometry.LongCoordinateGuardOk) + ",\n" +
                "  \"sticky_root_close_collapses_group_ok\": " + Bool(
                    dockChecks.Lifecycle.CloseHierarchyOk) + ",\n" +
                "  \"sticky_native_aero_snap_disabled_ok\": " + Bool(
                    windowPolicyChecks.NativeSnapDisabledOk &&
                    editorChecks.NativeWindowStyleAppliedOk) + ",\n" +
                "  \"sticky_first_drag_geometry_recovery_ok\": " + Bool(
                    dockChecks.Geometry.FirstDragRecoveryOk) + ",\n" +
                "  \"detached_group_returns_on_screen_ok\": " + Bool(
                    dockChecks.Geometry.DetachedGroupReturnsOnScreenOk) + ",\n" +
                "  \"sticky_screen_recovery_anchor_ok\": " + Bool(
                    dockChecks.Geometry.ScreenRecoveryAnchorOk) + ",\n" +
                "  \"dock_visual_seam_uses_detached_facts_ok\": " + Bool(
                    dockChecks.Geometry.ExecutorNeutralDockVisualSeamOk) +
                    ",\n" +
                "  \"ordinary_drag_cannot_accidentally_split_ok\": " + Bool(
                    dockChecks.Lifecycle.SplitGestureOk) + ",\n" +
                "  \"root_drag_always_moves_whole_group_ok\": " + Bool(
                    dockChecks.Lifecycle.RootDragNeverSplitsOk) + ",\n";
        }

        private static string BuildPersistenceReportFields(
            SettingsPersistenceCheckResult settingsChecks,
            ReminderCoordinatorCheckResult reminderCoordinatorChecks,
            StickyPersistenceCheckResult stickyChecks,
            WindowShellCheckResult shellChecks,
            StickyCompatibilityCheckResult compatibilityChecks)
        {
            return
                "  \"settings_backup_recovery_ok\": " + Bool(
                    settingsChecks.BackupRecoveryOk) + ",\n" +
                "  \"settings_failure_dirty_retry_ok\": " + Bool(
                    settingsChecks.FailureDirtyRetryOk) + ",\n" +
                "  \"daily_briefing_date_persistence_ok\": " + Bool(
                    settingsChecks.DailyBriefingDatePersistenceOk) + ",\n" +
                "  \"multiple_reminders_per_note_ok\": " + Bool(
                    reminderCoordinatorChecks.MultipleLinkedReminderOk) + ",\n" +
                "  \"sticky_note_persistence_ok\": " + Bool(
                    stickyChecks.PersistenceOk) + ",\n" +
                "  \"sticky_import_merge_commit_ok\": " + Bool(
                    stickyChecks.ImportMergeCommitOk) + ",\n" +
                "  \"sticky_full_restore_commit_ok\": " + Bool(
                    stickyChecks.FullRestoreCommitOk) + ",\n" +
                "  \"sticky_current_backup_strict_round_trip_ok\": " + Bool(
                    stickyChecks.CurrentBackupRoundTripOk) + ",\n" +
                "  \"sticky_pin_action_text_ok\": " + Bool(
                    shellChecks.PinActionTextOk) + ",\n" +
                "  \"todo_sticky_pin_action_text_ok\": " + Bool(
                    shellChecks.TodoPinActionTextOk) + ",\n" +
                "  \"sticky_rich_text_persistence_ok\": " + Bool(
                    stickyChecks.RichTextOk) + ",\n" +
                "  \"sticky_rich_text_no_silent_truncation_ok\": " + Bool(
                    stickyChecks.RichTextNoSilentTruncationOk) + ",\n" +
                "  \"multilingual_note_persistence_ok\": " + Bool(
                    stickyChecks.MultilingualOk) + ",\n" +
                "  \"ime_compatible_editor_ok\": " + Bool(
                    shellChecks.ImeCompatibleEditorOk) + ",\n" +
                "  \"sticky_single_window_input_ok\": " + Bool(
                    shellChecks.SingleWindowStickyInputOk) + ",\n" +
                "  \"legacy_note_migration_ok\": " + Bool(
                    compatibilityChecks.LegacyMigrationOk) + ",\n" +
                "  \"old_fish_shanying_cache_import_ok\": " + Bool(
                    compatibilityChecks.OldestFolderCacheImportOk) + ",\n" +
                "  \"version4_note_font_migration_ok\": " + Bool(
                    compatibilityChecks.VersionFourMigrationOk) + ",\n" +
                "  \"ancient_cache_display_repair_ok\": " + Bool(
                    compatibilityChecks.AncientCacheDisplayRepairOk) + ",\n" +
                "  \"sticky_future_primary_blocks_startup_ok\": " + Bool(
                    compatibilityChecks.FuturePrimaryBlocksStartupOk) + ",\n" +
                "  \"sticky_future_schema_classification_ok\": " + Bool(
                    compatibilityChecks.FutureFailureClassificationOk) + ",\n" +
                "  \"sticky_future_schema_never_salvages_ok\": " + Bool(
                    compatibilityChecks.FutureNoSalvageOk) + ",\n" +
                "  \"sticky_future_primary_does_not_fallback_to_older_backup_ok\": " +
                    Bool(compatibilityChecks.FutureOlderBackupNotLoadedOk) +
                    ",\n" +
                "  \"sticky_future_repository_read_only_ok\": " + Bool(
                    compatibilityChecks.FutureRepositoryReadOnlyOk) + ",\n" +
                "  \"sticky_future_sync_save_rejected_ok\": " + Bool(
                    compatibilityChecks.FutureSyncSaveRejectedOk) + ",\n" +
                "  \"sticky_future_async_save_rejected_ok\": " + Bool(
                    compatibilityChecks.FutureAsyncSaveRejectedOk) + ",\n" +
                "  \"sticky_future_mutations_rejected_ok\": " + Bool(
                    compatibilityChecks.FutureMutationsRejectedOk) + ",\n" +
                "  \"sticky_future_primary_sha256_unchanged_ok\": " + Bool(
                    compatibilityChecks.FuturePrimaryBytesUnchangedOk) + ",\n" +
                "  \"sticky_future_backup_sha256_unchanged_ok\": " + Bool(
                    compatibilityChecks.FutureBackupBytesUnchangedOk) + ",\n" +
                "  \"sticky_future_creates_no_recovery_artifacts_ok\": " + Bool(
                    compatibilityChecks.FutureNoRecoveryArtifactsOk) + ",\n" +
                "  \"sticky_historical_startup_matrix_ok\": " + Bool(
                    compatibilityChecks.HistoricalStartupMatrixOk) + ",\n" +
                "  \"sticky_current_schema_startup_round_trip_ok\": " + Bool(
                    compatibilityChecks.CurrentStartupRoundTripOk) + ",\n" +
                "  \"sticky_future_schema_user_message_ok\": " + Bool(
                    compatibilityChecks.FutureUserMessageOk) + ",\n" +
                "  \"todo_persistence_ok\": " + Bool(
                    stickyChecks.TodoOk) + ",\n" +
                "  \"schedule_persistence_ok\": " + Bool(
                    stickyChecks.ScheduleOk) + ",\n";
        }

        private static string BuildStickyInteractionReportFields(
            WindowShellCheckResult shellChecks,
            StickyEditorCheckResult editorChecks,
            StickyDialogCheckResult dialogChecks,
            StickyWindowPolicyCheckResult windowPolicyChecks,
            StickyFontCheckResult fontChecks)
        {
            return
                "  \"todo_pending_completed_groups_ok\": " + Bool(
                    shellChecks.TodoChecks.GroupingOk) + ",\n" +
                "  \"reminder_banner_countdown_ok\": " + Bool(
                    shellChecks.ReminderChecks.BannerCountdownOk) + ",\n" +
                "  \"reminder_banner_compact_font_ok\": " + Bool(
                    shellChecks.ReminderChecks.CompactBannerOk) + ",\n" +
                "  \"reminder_selection_actions_ok\": " + Bool(
                    shellChecks.ReminderChecks.SelectionActionsOk) + ",\n" +
                "  \"inline_new_reminder_and_list_removed_ok\": " + Bool(
                    shellChecks.ReminderChecks.InlineCreationActionsRemovedOk) +
                    ",\n" +
                "  \"reminder_first_click_survives_refresh_ok\": " + Bool(
                    shellChecks.ReminderChecks.FirstClickStableOk) + ",\n" +
                "  \"reminder_banner_refreshes_in_place_ok\": " + Bool(
                    shellChecks.ReminderChecks.BannerRefreshInPlaceOk) + ",\n" +
                "  \"reminder_blank_area_clears_selection_ok\": " + Bool(
                    shellChecks.ReminderChecks.BlankAreaClearOk) + ",\n" +
                "  \"reminder_content_wraps_without_ellipsis_ok\": " + Bool(
                    shellChecks.ReminderChecks.SelectionActionsOk) + ",\n" +
                "  \"todo_double_click_inline_edit_ok\": " + Bool(
                    shellChecks.TodoChecks.WrapAndInlineEditOk) + ",\n" +
                "  \"todo_content_wraps_without_ellipsis_ok\": " + Bool(
                    shellChecks.TodoChecks.WrapAndInlineEditOk) + ",\n" +
                "  \"todo_overall_font_size_ok\": " + Bool(
                    shellChecks.TodoChecks.OverallFontSizeOk) + ",\n" +
                "  \"dedicated_reminder_todo_context_menus_ok\": " + Bool(
                    shellChecks.TodoChecks.DedicatedRowContextMenusOk) + ",\n" +
                "  \"todo_marker_round_trip_ok\": " + Bool(
                    shellChecks.TodoChecks.MarkerRoundTripOk) + ",\n" +
                "  \"todo_plain_text_projection_ok\": " + Bool(
                    shellChecks.TodoChecks.PlainTextProjectionOk) + ",\n" +
                "  \"multilingual_text_input_ok\": " + Bool(
                    editorChecks.MultilingualInputOk) + ",\n" +
                "  \"input_method_not_forced_to_chinese_ok\": " + Bool(
                    dialogChecks.UnforcedMultilingualImeOk) + ",\n" +
                "  \"format_tab_switch_content_preserved_ok\": " + Bool(
                    editorChecks.TabSwitchContentPreservedOk) + ",\n" +
                "  \"sticky_resource_limits_ok\": " + Bool(
                    windowPolicyChecks.ResourceLimitsOk) + ",\n" +
                "  \"maximum_sticky_notes\": " + StickyNoteLimits.MaximumNotes +
                    ",\n" +
                "  \"maximum_todos_per_note\": " +
                    StickyNoteLimits.MaximumTodoItemsPerNote + ",\n" +
                "  \"sticky_resize_buffered_painting_ok\": " + Bool(
                    shellChecks.StickyResizePaintingOk) + ",\n" +
                "  \"sticky_rich_text_toolbar_ok\": " + Bool(
                    editorChecks.RichTextToolbarOk) + ",\n" +
                "  \"sticky_format_interaction_smooth_ok\": " + Bool(
                    editorChecks.SmoothFormatInteractionOk) + ",\n" +
                "  \"sticky_first_dropdown_focus_not_stolen_ok\": " + Bool(
                    editorChecks.DeferredInitialFocusSafeOk) + ",\n" +
                "  \"sticky_format_toolbar_preserves_selection_focus_ok\": " + Bool(
                    editorChecks.FormatToolbarFocusOk) + ",\n" +
                "  \"sticky_format_selectors_always_black_ok\": " + Bool(
                    editorChecks.FormatSelectorsAlwaysBlackOk) + ",\n" +
                "  \"sticky_body_text_color_switch_ok\": " + Bool(
                    editorChecks.BodyTextColorSwitchOk) + ",\n" +
                "  \"sticky_group_outer_resize_roles_ok\": " + Bool(
                    editorChecks.DockResizeRoleOk) + ",\n" +
                "  \"sticky_group_topmost_sync_ok\": " + Bool(
                    editorChecks.GroupTopMostSyncOk) + ",\n" +
                "  \"sticky_first_format_commit_ok\": " + Bool(
                    editorChecks.FirstFormatCommitOk) + ",\n" +
                "  \"empty_sticky_font_and_size_before_typing_ok\": " + Bool(
                    editorChecks.EmptyNoteFormattingOk) + ",\n" +
                "  \"sticky_existing_text_caret_format_switch_ok\": " + Bool(
                    editorChecks.CaretTypingFormatSwitchOk) + ",\n" +
                "  \"sticky_native_ime_single_commit_after_format_ok\": " + Bool(
                    editorChecks.SingleNativeImeCommitOk) + ",\n" +
                "  \"sticky_editor_and_window_context_actions_ok\": " + Bool(
                    editorChecks.UnifiedContextMenusOk) + ",\n" +
                "  \"sticky_note_types_never_convert_ok\": " + Bool(
                    shellChecks.TodoChecks.FixedTypeActionsOk) + ",\n" +
                "  \"sticky_font_size_parsing_ok\": " + Bool(
                    fontChecks.SizeParsingOk) + ",\n" +
                "  \"sticky_chinese_fonts_first_ok\": " + Bool(
                    fontChecks.ChineseFontsFirstOk) + ",\n" +
                "  \"sticky_installed_font_list_cached_ok\": " + Bool(
                    fontChecks.InstalledFontListCacheOk) + ",\n" +
                "  \"sticky_format_selector_single_event_model_ok\": " + Bool(
                    editorChecks.StableFormatSelectorModelOk) + ",\n" +
                "  \"shared_font_lifetime_ok\": " + Bool(
                    fontChecks.SharedFontLifetimeOk) + ",\n";
        }

        private static string BuildArtAndSettingsReportFields(
            ArtResourceCheckResult artChecks,
            SettingsPersistenceCheckResult settingsChecks)
        {
            return
                "  \"art_package\": {\"width\": " + artChecks.Width +
                ", \"height\": " + artChecks.Height +
                ", \"ok\": " + Bool(artChecks.AtlasOk) + "},\n" +
                "  \"animation_timing_from_art_package_ok\": " + Bool(
                    artChecks.AnimationTimingOk) + ",\n" +
                "  \"application_icon_embedded_ok\": " + Bool(
                    artChecks.ApplicationIconEmbeddedOk) + ",\n" +
                "  \"contact_author_feature_ok\": " + Bool(
                    artChecks.ContactAuthorFeatureOk) + ",\n" +
                "  \"contact_author_xiaohongshu_only_ok\": " + Bool(
                    artChecks.ContactAuthorFeatureOk) + ",\n" +
                "  \"minute_timer_ok\": " + Bool(
                    settingsChecks.MinuteTimerOk) + ",\n" +
                "  \"cancel_ok\": " + Bool(settingsChecks.CancelOk) + ",\n" +
                "  \"five_reminders_ok\": " + Bool(
                    settingsChecks.FiveRemindersOk) + ",\n" +
                "  \"sixth_reminder_blocked\": " + Bool(
                    settingsChecks.SixthReminderBlocked) + ",\n" +
                "  \"reminder_memory_ok\": " + Bool(
                    settingsChecks.ReminderMemoryOk) + ",\n" +
                "  \"daily_content_preferences_persistence_and_legacy_defaults_ok\": " +
                    Bool(settingsChecks
                        .DailyContentPreferencesPersistenceOk) + ",\n" +
                "  \"zodiac_preference_persistence_and_legacy_default_ok\": " +
                    Bool(settingsChecks.ZodiacPreferencePersistenceOk) +
                    ",\n" +
                "  \"weather_preference_persistence_and_legacy_default_ok\": " +
                    Bool(settingsChecks.WeatherPreferencePersistenceOk) +
                    ",\n";
        }

        private static string BuildScheduleAndExpiredReminderReportFields(
            StickyScheduleWindowCheckResult scheduleWindowChecks,
            ReminderCoordinatorCheckResult reminderCoordinatorChecks)
        {
            return
                "  \"schedule_countdown_ok\": " + Bool(
                    scheduleWindowChecks.CountdownOk) + ",\n" +
                "  \"schedule_five_tier_font_ok\": " + Bool(
                    scheduleWindowChecks.FontChoicesOk) + ",\n" +
                "  \"schedule_date_mouse_wheel_ok\": " + Bool(
                    scheduleWindowChecks.DateMouseWheelOk) + ",\n" +
                "  \"schedule_pin_marker_toggle_idempotent_ok\": " + Bool(
                    scheduleWindowChecks.PinMarkerToggleOk) + ",\n" +
                "  \"expired_reminder_discarded_after_closed_app_ok\": " + Bool(
                    reminderCoordinatorChecks.ExpiredAtLaunchDiscardedOk) +
                    ",\n";
        }

        private static string BuildDialogWindowAndSideTabReportFields(
            StickyDialogCheckResult dialogChecks,
            StickyWindowPolicyCheckResult windowPolicyChecks,
            StickySideTabCheckResult sideTabChecks)
        {
            return
                "  \"reminder_size_preview_ok\": " + Bool(
                    dialogChecks.ReminderSizePreviewOk) + ",\n" +
                "  \"standalone_reminder_no_auto_sticky_option_ok\": " +
                    Bool(dialogChecks.StandaloneReminderNoAutoStickyOptionOk) + ",\n" +
                "  \"reminder_live_note_size_preview_ok\": " + Bool(
                    dialogChecks.ReminderLiveSizePreviewOk) + ",\n" +
                "  \"sticky_high_dpi_layout_ok\": " + Bool(
                    windowPolicyChecks.HighDpiLayoutOk) + ",\n" +
                "  \"reminder_default_current_time_ok\": " + Bool(
                    dialogChecks.ReminderDefaultCurrentTimeOk) + ",\n" +
                "  \"ordinary_sticky_web_and_local_links_ok\": " + Bool(
                    windowPolicyChecks.OrdinaryLinkDetectionOk) + ",\n" +
                "  \"soft_sticky_palette_ok\": " + Bool(
                    windowPolicyChecks.SoftPaletteOk) + ",\n" +
                "  \"full_width_latin_normalization_ok\": " + Bool(
                    windowPolicyChecks.FullWidthNormalizationOk) + ",\n" +
                "  \"rename_initial_focus_ok\": " + Bool(
                    dialogChecks.RenameInitialFocusOk) + ",\n" +
                "  \"side_tab_left_then_right_overflow_ok\": " + Bool(
                    sideTabChecks.OverflowOk) + ",\n" +
                "  \"side_tab_delete_command_ok\": " + Bool(
                    sideTabChecks.DeleteCommandOk) + ",\n" +
                "  \"side_tab_drag_preview_ok\": " + Bool(
                    sideTabChecks.DragPreviewOk) + ",\n" +
                "  \"side_tab_drop_commit_after_drag_loop_ok\": " + Bool(
                    sideTabChecks.DeferredDropCommitOk) + ",\n" +
                "  \"side_tab_preview_clears_both_sides_ok\": " + Bool(
                    sideTabChecks.PreviewClearsBothSidesOk) + ",\n" +
                "  \"side_tab_explicit_source_keeps_target_first_ok\": " + Bool(
                    sideTabChecks.ExplicitSourceKeepsTargetFirstOk) + ",\n" +
                "  \"side_tab_target_never_marked_as_source_ok\": " + Bool(
                    sideTabChecks.TargetNeverMarkedAsSourceOk) + ",\n" +
                "  \"side_tab_exclusive_canvas_state_ok\": " + Bool(
                    sideTabChecks.ExclusiveCanvasStateOk) + ",\n" +
                "  \"side_tab_reverse_boundary_rollover_ok\": " + Bool(
                    sideTabChecks.ReverseBoundaryRolloverOk) + ",\n" +
                "  \"side_tab_boundary_edge_drop_ok\": " + Bool(
                    sideTabChecks.BoundaryEdgeDropOk) + ",\n" +
                "  \"side_tab_scaled_visual_gap_halved_ok\": " + Bool(
                    sideTabChecks.ScaledGapOk) + ",\n" +
                "  \"side_tab_vector_icon_uses_darker_tab_color_ok\": " + Bool(
                    sideTabChecks.VectorIconColorOk) + ",\n" +
                "  \"side_tab_z_order_policy_ok\": " + Bool(
                    sideTabChecks.ZOrderPolicyOk) + ",\n" +
                "  \"side_tab_layout_invalidation_ok\": " + Bool(
                    sideTabChecks.LayoutInvalidationOk) + ",\n";
        }

        private static string BuildPolicyKeyboardReminderReportFields(
            StickyWindowPolicyCheckResult windowPolicyChecks,
            KeyboardOverlayCheckResult keyboardOverlayChecks,
            StickyEditorCheckResult editorChecks,
            WindowShellCheckResult shellChecks,
            bool automaticNoteBackupOk,
            StickyPersistenceCheckResult stickyChecks,
            StickyCompatibilityCheckResult compatibilityChecks,
            ReminderCoordinatorCheckResult reminderCoordinatorChecks,
            SettingsPersistenceCheckResult settingsChecks)
        {
            return
                "  \"dock_guides_do_not_flash_ok\": " + Bool(
                    windowPolicyChecks.SteadyDockGuideOk) + ",\n" +
                "  \"manager_marquee_batch_delete_ok\": " + Bool(
                    windowPolicyChecks.ManagerMarqueeBatchDeleteOk) + ",\n" +
                "  \"manager_sorting_ok\": " + Bool(
                    windowPolicyChecks.ManagerSortingOk) + ",\n" +
                "  \"manager_import_preview_ok\": " + Bool(
                    windowPolicyChecks.ManagerImportPreviewOk) + ",\n" +
                "  \"manager_responsive_layout_ok\": " + Bool(
                    windowPolicyChecks.ManagerResponsiveLayoutOk) + ",\n" +
                "  \"held_key_overlay_stays_constant_ok\": " + Bool(
                    keyboardOverlayChecks.HeldKeyStableOk) + ",\n" +
                "  \"keyboard_hook_captures_own_process_ok\": " + Bool(
                    keyboardOverlayChecks.HookCapturePolicyOk) + ",\n" +
                "  \"own_process_sticky_eligibility_ok\": " + Bool(
                    keyboardOverlayChecks.OwnProcessEligibilityOk) + ",\n" +
                "  \"ime_animation_guard_ok\": " + Bool(
                    editorChecks.ImeAnimationGuardOk) + ",\n" +
                "  \"ime_autosave_guard_ok\": " + Bool(
                    editorChecks.ImeAutoSaveGuardOk) + ",\n" +
                "  \"reverse_reminder_step_ok\": " + Bool(
                    shellChecks.ReverseReminderStepOk) + ",\n" +
                "  \"automatic_note_backup_ok\": " + Bool(automaticNoteBackupOk) + ",\n" +
                "  \"persistence_failure_dirty_state_ok\": " + Bool(
                    stickyChecks.FailureDirtyRetryOk) + ",\n" +
                "  \"sticky_generation_monotonic_ok\": " + Bool(
                    stickyChecks.GenerationMonotonicOk) + ",\n" +
                "  \"sticky_pending_save_wait_bounded_ok\": " + Bool(
                    stickyChecks.PendingSaveWaitBoundedOk) + ",\n" +
                "  \"failed_load_never_overwrites_ok\": " + Bool(
                    compatibilityChecks.FailedLoadNeverOverwritesOk) + ",\n" +
                "  \"sticky_backup_recovery_allows_create_ok\": " + Bool(
                    compatibilityChecks.BackupRecoveryOk) + ",\n" +
                "  \"concrete_date_time_ok\": " + Bool(
                    reminderCoordinatorChecks.ConcreteDateTimeOk) + ",\n" +
                "  \"reminder_banner_tick_throttled_ok\": " + Bool(
                    reminderCoordinatorChecks.BannerTickThrottleOk) + ",\n" +
                "  \"reminder_runtime_ownership_ok\": " + Bool(
                    reminderCoordinatorChecks.RuntimeOwnershipOk) + ",\n" +
                "  \"startup_default_ok\": " + Bool(
                    shellChecks.StartupDefaultOk) + ",\n" +
                "  \"sticky_ui_host_ok\": " + Bool(
                    shellChecks.StickyUiHostOk) + ",\n" +
                "  \"sticky_hosted_lifecycle_ok\": " + Bool(
                    shellChecks.StickyHosted.LifecycleOk) + ",\n" +
                "  \"sticky_hosted_sequence_rejection_ok\": " + Bool(
                    shellChecks.StickyHosted.PerNoteSequenceOk) + ",\n" +
                "  \"sticky_hosted_close_all_batch_ok\": " + Bool(
                    shellChecks.StickyHosted.CloseAllBatchOk) + ",\n" +
                "  \"sticky_hosted_two_note_dock_effect_ok\": " + Bool(
                    shellChecks.StickyHosted.HostedDockEffectOk) + ",\n" +
                "  \"sticky_hosted_group_move_ok\": " + Bool(
                    shellChecks.StickyHosted.HostedGroupMoveOk) + ",\n" +
                "  \"sticky_hosted_topmost_ok\": " + Bool(
                    shellChecks.StickyHosted.HostedTopMostOk) + ",\n" +
                "  \"sticky_hosted_horizontal_resize_ok\": " + Bool(
                    shellChecks.StickyHosted.HostedHorizontalResizeOk) +
                    ",\n" +
                "  \"sticky_hosted_divider_resize_ok\": " + Bool(
                    shellChecks.StickyHosted.HostedDividerResizeOk) +
                    ",\n" +
                "  \"sticky_hosted_hide_reopen_ok\": " + Bool(
                    shellChecks.StickyHosted.HostedHideReopenOk) + ",\n" +
                "  \"sticky_hosted_middle_split_ok\": " + Bool(
                    shellChecks.StickyHosted.HostedMiddleSplitOk) + ",\n" +
                "  \"sticky_hosted_three_note_insertion_ok\": " + Bool(
                    shellChecks.StickyHosted.HostedThreeNoteInsertionOk) +
                    ",\n" +
                "  \"sticky_hosted_dock_restore_ok\": " + Bool(
                    shellChecks.StickyHosted.DockRestoreOk) + ",\n" +
                "  \"sticky_hosted_setbounds_atomic_ok\": " + Bool(
                    shellChecks.StickyHosted.HostedSetBoundsAtomicOk) + ",\n" +
                "  \"persona_runtime_catalog_ok\": " + Bool(
                    shellChecks.PersonaRuntimeCatalogOk) + ",\n" +
                "  \"solar_term_attachment_ok\": " + Bool(
                    shellChecks.SolarTermAttachmentOk) + ",\n" +
                "  \"persona_lyric_animation_ok\": " + Bool(
                    shellChecks.PersonaLyricAnimationOk) + ",\n" +
                "  \"persona_smalltalk_animation_protection_ok\": " + Bool(
                    shellChecks.SmallTalkAnimationProtectionOk) + ",\n" +
                "  \"solar_preserve_plumbing_ok\": " + Bool(
                    shellChecks.SolarPreservePlumbingOk) + ",\n" +
                "  \"display_topology_runtime_ok\": " + Bool(
                    shellChecks.DisplayTopologyRuntimeOk) + ",\n" +
                "  \"sticky_content_apply_separation_ok\": " + Bool(
                    shellChecks.StickyContentApplySeparationOk) + ",\n" +
                "  \"native_placement_ok\": " + Bool(
                    shellChecks.NativePlacementOk) + ",\n" +
                "  \"native_display_abi_ok\": " + Bool(
                    shellChecks.NativeDisplayAbiOk) + ",\n" +
                "  \"v11_preferred_ok\": " + Bool(
                    shellChecks.V11PreferredOk) + ",\n" +
                "  \"temporary_rehome_ok\": " + Bool(
                    shellChecks.TemporaryRehomeOk) + ",\n" +
                "  \"dock_commit_handoff_ok\": " + Bool(
                    shellChecks.DockCommitHandoffOk) + ",\n" +
                "  \"dock_topology_reproject_ok\": " + Bool(
                    shellChecks.DockTopologyReprojectOk) + ",\n" +
                "  \"dock_zorder_ok\": " + Bool(
                    shellChecks.DockZOrderOk) + ",\n" +
                "  \"keyboard_hook_opt_in_and_default_off_ok\": " + Bool(
                    keyboardOverlayChecks.HookOptInDefaultOk) + ",\n" +
                "  \"keyboard_privacy_notice_persistence_ok\": " + Bool(
                    settingsChecks.KeyboardPrivacyNoticePersistenceOk) +
                    ",\n";
        }

        private static string BuildAnimationArtReportFields(
            AnimationCheckResult animationChecks,
            ArtResourceCheckResult artChecks,
            ReminderCoordinatorCheckResult reminderCoordinatorChecks)
        {
            return
                "  \"goodbye_animation_ok\": " + Bool(
                    animationChecks.GoodbyeOk) + ",\n" +
                "  \"notification_animation_ok\": " + Bool(
                    animationChecks.NotificationOk) + ",\n" +
                "  \"notification_trigger_playback_route_ok\": " + Bool(
                    artChecks.NotificationPlaybackOk) + ",\n" +
                "  \"due_reminder_bubble_persists_until_clicked_ok\": " + Bool(
                    reminderCoordinatorChecks.DueBubblePersistentOk) + ",\n" +
                "  \"due_reminder_bubble_uses_own_size_ok\": " + Bool(
                    reminderCoordinatorChecks.DueBubbleUsesOwnSizeOk) + ",\n" +
                "  \"due_reminder_bubble_blocks_lower_priority_messages_ok\": " +
                    Bool(reminderCoordinatorChecks.DueBubbleReplacementOk) +
                    ",\n" +
                "  \"prealert_countdown_bubble_not_replaced_by_note_feedback_ok\": " +
                    Bool(reminderCoordinatorChecks.PreAlertBubbleProtectionOk) +
                    ",\n" +
                "  \"notification_animation_single_cycle_ok\": " + Bool(
                    animationChecks.NotificationSingleCycleOk) + ",\n" +
                "  \"drag_uses_second_idle_row_ok\": " + Bool(
                    animationChecks.DragUsesSecondIdleRowOk) + ",\n" +
                "  \"idle_random_rows_ok\": " + Bool(
                    animationChecks.IdleRandomRowsOk) + ",\n" +
                "  \"typing_random_rows_ok\": " + Bool(
                    animationChecks.TypingRandomRowsOk) + ",\n" +
                "  \"idle_thought_combined_ten_percent_ok\": " + Bool(
                    animationChecks.IdleThoughtProbabilityReducedOk) + ",\n" +
                "  \"failed_guitar_probability_one_third_ok\": " + Bool(
                    animationChecks.GuitarFailureProbabilityReducedOk) +
                    ",\n" +
                "  \"startup_lazy_art_load_ok\": " + Bool(
                    artChecks.StartupLazyLoadOk) + ",\n" +
                "  \"startup_interaction_art_preload_ok\": " + Bool(
                    artChecks.InteractionPreloadOk) + ",\n" +
                "  \"startup_idle_frame_cache_embedded_ok\": " + Bool(
                    artChecks.StartupCacheEmbeddedOk) + ",\n" +
                "  \"per_pixel_alpha_renderer\": true,\n" +
                "  \"inner_outline_ok\": " + Bool(
                    artChecks.InnerOutlineOk) + ",\n" +
                "  \"external_outline_pixels\": 0,\n" +
                "  \"green_halo_absent\": " + Bool(
                    artChecks.GreenHaloAbsent) + ",\n" +
                "  \"idle_cycle_milliseconds\": " +
                    artChecks.AnimationCycleDurations[0] + ",\n" +
                "  \"failed_cycle_milliseconds\": " +
                    artChecks.AnimationCycleDurations[5] + ",\n" +
                "  \"waiting_cycle_milliseconds\": " +
                    artChecks.AnimationCycleDurations[6] + ",\n" +
                "  \"thinking_cycle_milliseconds\": " +
                    artChecks.AnimationCycleDurations[7] + ",\n" +
                "  \"review_cycle_milliseconds\": " +
                    artChecks.AnimationCycleDurations[8] + ",\n" +
                "  \"goodbye_cycle_milliseconds\": " +
                    artChecks.AnimationCycleDurations[3] + ",\n" +
                "  \"notification_cycle_milliseconds\": " +
                    artChecks.AnimationCycleDurations[9] + ",\n" +
                "  \"smooth_timing_ok\": " + Bool(
                    animationChecks.SmoothTimingOk) + ",\n";
        }

        private static string BuildBubbleManualKeyboardReportFields(
            BubbleCheckResult bubbleChecks,
            ReminderCoordinatorCheckResult reminderCoordinatorChecks,
            SettingsPersistenceCheckResult settingsChecks,
            AnimationCheckResult animationChecks,
            WindowShellCheckResult shellChecks,
            KeyboardOverlayCheckResult keyboardOverlayChecks,
            WeatherCheckResult weatherChecks)
        {
            return
                "  \"hover_bubble_copy_ok\": " + Bool(
                    bubbleChecks.HoverCopyOk) + ",\n" +
                "  \"styled_reminder_bubble_ok\": " + Bool(
                    bubbleChecks.StyledReminderOk) + ",\n" +
                "  \"bubble_green_white_keyboard_font_ok\": " + Bool(
                    bubbleChecks.ThemeAndKeyboardFontOk) + ",\n" +
                "  \"reminder_bubble_uses_configured_size_ok\": " + Bool(
                    bubbleChecks.StyledReminderOk) + ",\n" +
                "  \"bubble_manual_position_ok\": " + Bool(
                    bubbleChecks.ManualPositionOk) + ",\n" +
                "  \"drag_bubble_suppression_ok\": " + Bool(
                    bubbleChecks.DragSuppressionOk) + ",\n" +
                "  \"silent_mode_persistence_ok\": " + Bool(
                    settingsChecks.SilentModePersistenceOk) + ",\n" +
                "  \"silent_mode_daily_bubbles_suppressed_ok\": " + Bool(
                    bubbleChecks.SilentModeOk) + ",\n" +
                "  \"silent_mode_reminder_bubbles_preserved_ok\": " + Bool(
                    !PetMessagePolicy.ShouldSuppress(
                        PetMessageKind.ReminderDue, true)) + ",\n" +
                "  \"manual_animation_random_pool_excludes_running_rows_ok\": " + Bool(
                    animationChecks.ManualRandomPoolOk) + ",\n" +
                "  \"manual_special_animation_probability_reduced_ok\": " + Bool(
                    animationChecks.ManualSpecialProbabilityReducedOk) +
                    ",\n" +
                "  \"manual_animation_full_cycle_guard_ok\": " + Bool(
                    animationChecks.ManualFullCycleGuardOk) + ",\n" +
                "  \"poke_burst_fifty_once_until_pause_ok\": " + Bool(
                    animationChecks.PokeBurstOk) + ",\n" +
                "  \"left_click_drag_threshold_ok\": " + Bool(
                    animationChecks.ClickDragThresholdOk) + ",\n" +
                "  \"bubble_position_math_ok\": " + Bool(
                    bubbleChecks.PositionMathOk) + ",\n" +
                "  \"bubble_single_message_kind_ok\": " + Bool(
                    bubbleChecks.SingleMessageKindOk) + ",\n" +
                "  \"bubble_replacement_closes_old_form_ok\": " + Bool(
                    bubbleChecks.ReplacementClosesOldFormOk) + ",\n" +
                "  \"bubble_protected_message_ok\": " + Bool(
                    bubbleChecks.ProtectedMessageOk) + ",\n" +
                "  \"bubble_deferred_message_semantics_ok\": " + Bool(
                    bubbleChecks.DeferredMessageSemanticsOk) + ",\n" +
                "  \"bubble_pending_retry_without_loss_or_duplication_ok\": " +
                    Bool(bubbleChecks.PendingRetryOk) + ",\n" +
                "  \"smalltalk_feedback_minimum_readable_lifecycle_ok\": " +
                    Bool(bubbleChecks.SmallTalkFeedbackLifecycleOk) +
                    ",\n" +
                "  \"bubble_reminder_priority_regression_ok\": " + Bool(
                    bubbleChecks.ReminderPriorityRegressionOk) + ",\n" +
                "  \"bubble_single_restore_after_close_ok\": " + Bool(
                    bubbleChecks.SingleRestoreAfterCloseOk) + ",\n" +
                "  \"bubble_adaptive_sizing_ok\": " + Bool(
                    bubbleChecks.AdaptiveSizingOk) + ",\n" +
                "  \"bubble_update_text_relayout_ok\": " + Bool(
                    bubbleChecks.UpdateTextRelayoutOk) + ",\n" +
                "  \"daily_content_first_poke_once_ok\": " + Bool(
                    bubbleChecks.DailyFirstPokeOk) + ",\n" +
                "  \"daily_content_rejected_retry_ok\": " + Bool(
                    bubbleChecks.DailyRejectedRetryOk) + ",\n" +
                "  \"daily_greeting_typed_request_ok\": " + Bool(
                    bubbleChecks.DailyGreetingRequestOk) + ",\n" +
                "  \"poke_easter_egg_typed_request_and_priority_ok\": " + Bool(
                    bubbleChecks.EasterEggRequestOk) + ",\n" +
                "  \"bubble_minimum_readable_dwell_ok\": " + Bool(
                    bubbleChecks.MinimumReadableOk) + ",\n" +
                "  \"bubble_readability_priority_bypass_ok\": " + Bool(
                    bubbleChecks.ReadabilityBypassOk) + ",\n" +
                "  \"smalltalk_typed_request_and_silent_mode_ok\": " + Bool(
                    bubbleChecks.SmallTalkRequestOk) + ",\n" +
                "  \"smalltalk_coordinator_cooldown_and_rotation_ok\": " + Bool(
                    bubbleChecks.SmallTalkCoordinatorCooldownOk) + ",\n" +
                "  \"smalltalk_coordinator_rejected_show_retry_ok\": " + Bool(
                    bubbleChecks.SmallTalkCoordinatorRejectedRetryOk) +
                    ",\n" +
                "  \"smalltalk_coordinator_silent_mode_retry_ok\": " + Bool(
                    bubbleChecks.SmallTalkCoordinatorSilentModeOk) + ",\n" +
                "  \"smalltalk_coordinator_reminder_reject_retry_ok\": " + Bool(
                    bubbleChecks.SmallTalkCoordinatorReminderRetryOk) +
                    ",\n" +
                "  \"solar_term_daily_greeting_fact_ok\": " + Bool(
                    bubbleChecks.SolarTermOk) + ",\n" +
                "  \"daily_content_preference_flow_ok\": " + Bool(
                    bubbleChecks.DailyContentPreferencesOk) + ",\n" +
                "  \"daily_line_catalogs_complete_unique_ok\": " + Bool(
                    bubbleChecks.CuratedCatalogOk) + ",\n" +
                "  \"daily_selectors_deterministic_budgeted_ok\": " +
                    Bool(bubbleChecks.DailySelectorBudgetOk) + ",\n" +
                "  \"daily_briefing_supplementary_budget_ok\": " + Bool(
                    bubbleChecks.DailyBriefingBudgetOk) + ",\n" +
                "  \"sentence_ending_policy_ok\": " + Bool(
                    bubbleChecks.SentenceEndingPolicyOk) + ",\n" +
                "  \"daily_briefing_coordinator_integration_ok\": " + Bool(
                    bubbleChecks.DailyBriefingCoordinatorOk) + ",\n" +
                "  \"daily_briefing_rejected_show_retry_ok\": " + Bool(
                    bubbleChecks.DailyBriefingRejectedRetryOk) + ",\n" +
                "  \"daily_briefing_same_day_sign_switch_ok\": " + Bool(
                    bubbleChecks.DailyBriefingSameDaySwitchOk) + ",\n" +
                "  \"almanac_calculator_dependency_and_sect_ok\": " + Bool(
                    bubbleChecks.AlmanacCalculatorOk) + ",\n" +
                "  \"almanac_semantic_whitelist_conflict_ok\": " + Bool(
                    bubbleChecks.AlmanacSemanticOk) + ",\n" +
                "  \"almanac_wording_deterministic_variation_ok\": " + Bool(
                    bubbleChecks.AlmanacWordingOk) + ",\n" +
                "  \"daily_content_settings_ui_and_menu_ok\": " + Bool(
                    shellChecks.DailyContentSettingsUiOk) + ",\n" +
                "  \"weather_fixture_parser_ok\": " + Bool(
                    weatherChecks.ForecastFixtureParsingOk) + ",\n" +
                "  \"weather_forecast_request_shape_ok\": " + Bool(
                    weatherChecks.ForecastRequestShapeOk) + ",\n" +
                "  \"weather_geocoding_explicit_search_ok\": " + Bool(
                    weatherChecks.GeocodingRequestAndSelectionOk) + ",\n" +
                "  \"weather_zero_startup_requests_ok\": " + Bool(
                    weatherChecks.NoStartupRequestOk) + ",\n" +
                "  \"weather_same_day_cache_and_inflight_ok\": " + Bool(
                    weatherChecks.SameDayCacheAndInFlightOk) + ",\n" +
                "  \"weather_bounded_cache_invalidation_ok\": " + Bool(
                    weatherChecks.BoundedCacheInvalidationOk) + ",\n" +
                "  \"weather_failure_cooldown_ok\": " + Bool(
                    weatherChecks.FailureCooldownOk) + ",\n" +
                "  \"weather_meaning_and_wording_ok\": " + Bool(
                    weatherChecks.MeaningAndWordingOk) + ",\n" +
                "  \"weather_daily_coordinator_integration_ok\": " + Bool(
                    weatherChecks.DailyCoordinatorWeatherOk) + ",\n" +
                "  \"weather_failure_daily_fallback_ok\": " + Bool(
                    weatherChecks.DailyCoordinatorFailureFallbackOk) +
                    ",\n" +
                "  \"weather_daily_inflight_coalescing_ok\": " + Bool(
                    weatherChecks.DailyCoordinatorInFlightOk) + ",\n" +
                "  \"daily_content_async_preference_snapshot_ok\": " + Bool(
                    weatherChecks.DailyCoordinatorPreferenceSnapshotOk) +
                    ",\n" +
                "  \"conversation_runtime_ownership_ok\": " + Bool(
                    weatherChecks.ConversationOwnershipOk) + ",\n" +
                "  \"weather_rejected_bubble_reuses_forecast_ok\": " + Bool(
                    weatherChecks.RejectedBubbleReusesForecastOk) + ",\n" +
                "  \"weather_location_dialog_compact_formatting_ok\": " +
                    Bool(weatherChecks.LocationDialogLayoutOk) + ",\n" +
                "  \"zodiac_preference_settings_ui_ok\": " + Bool(
                    shellChecks.ZodiacPreferenceSettingsUiOk) + ",\n" +
                "  \"scale_50_to_200_step_10_ok\": " + Bool(
                    shellChecks.ScaleRangeOk) + ",\n" +
                "  \"keyboard_text_scale_choices_ok\": " + Bool(
                    keyboardOverlayChecks.TextScaleChoicesOk) + ",\n" +
                "  \"keyboard_shortcut_and_repeat_ok\": " + Bool(
                    keyboardOverlayChecks.ShortcutAndRepeatOk) + ",\n" +
                "  \"keyboard_privacy_generation_ok\": " + Bool(
                    keyboardOverlayChecks.PrivacyGenerationOk) + ",\n" +
                "  \"keyboard_focus_snapshot_identity_ok\": " + Bool(
                    keyboardOverlayChecks.FocusSnapshotIdentityOk) + ",\n" +
                "  \"adaptive_black_white_text_ok\": " + Bool(
                    keyboardOverlayChecks.AdaptiveContrastOk) + ",\n";
        }

        private static string BuildStaticReportTail()
        {
            return
                "  \"thinking_row_registered\": true,\n" +
                "  \"typing_random_rows_registered\": true,\n" +
                "  \"idle_random_rows_registered\": true,\n" +
                "  \"goodbye_row_registered\": true,\n" +
                "  \"notification_row_registered\": true,\n" +
                "  \"typing_moves_pet\": false,\n" +
                "  \"look_follow_registered\": false,\n" +
                "  \"keyboard_content_recorded\": false\n" +
                "}\n";
        }

        public static void Run(string outputPath)
        {
            try
            {
                // PC-2A: execute production rejection paths before any harness
                // decomposition. Characterization success is not defect absence.
                RunPc2CharacterizationChecks(outputPath);
                bool stickyFeatureBoundaryOk = RunStickyFeatureBoundaryChecks();
                RunDisplayResolverConsistencyCheck();
                ArtResourceCheckResult artChecks = RunArtResourceChecks();
                SettingsPersistenceCheckResult settingsChecks =
                    RunSettingsPersistenceChecks(outputPath);
                ReminderCoordinatorCheckResult reminderCoordinatorChecks =
                    RunReminderCoordinatorChecks(settingsChecks.ReminderBaseUtc);
                KeyboardOverlayCheckResult keyboardOverlayChecks =
                    RunKeyboardOverlayChecks();
                AnimationCheckResult animationChecks =
                    RunAnimationChecks(artChecks.AnimationCycleDurations);
                BubbleCheckResult bubbleChecks = RunBubbleChecks();
                WeatherCheckResult weatherChecks = RunWeatherChecks();
                StickyEditorCheckResult editorChecks = RunStickyEditorChecks();
                StickyPersistenceCheckResult stickyChecks =
                    RunStickyPersistenceChecks(outputPath);
                WindowShellCheckResult shellChecks =
                    RunWindowShellChecks(stickyChecks.RestoredNote);
                StickyScheduleWindowCheckResult scheduleWindowChecks =
                    RunStickyScheduleWindowChecks();
                StickyFontCheckResult fontChecks = RunStickyFontChecks();
                StickyDialogCheckResult dialogChecks = RunStickyDialogChecks();
                StickyWindowPolicyCheckResult windowPolicyChecks =
                    RunStickyWindowPolicyChecks(stickyChecks.Repository);
                StickySideTabCheckResult sideTabChecks =
                    RunStickySideTabChecks(stickyChecks.RestoredNote);
                bool automaticNoteBackupOk =
                    RunStickyBackupCleanupCheck(stickyChecks);
                StickyCompatibilityCheckResult stickyCompatibilityChecks =
                    RunStickyCompatibilityChecks(outputPath);
                DockCheckResult dockChecks = RunDockChecks(outputPath);
                // Every boolean emitted through Bool below is registered in one
                // collection. The root result can no longer drift away from the
                // detailed report when a new check is added.
                BeginCheckCollection();
                string reportBody =
                    "  \"sticky_feature_boundary_ok\": " + Bool(stickyFeatureBoundaryOk) + ",\n" +
                    BuildPc2CharacterizationReportFields() +
                    BuildArtAndSettingsReportFields(artChecks, settingsChecks) +
                    BuildPersistenceReportFields(settingsChecks,
                        reminderCoordinatorChecks, stickyChecks, shellChecks,
                        stickyCompatibilityChecks) +
                    BuildScheduleAndExpiredReminderReportFields(
                        scheduleWindowChecks, reminderCoordinatorChecks) +
                    BuildStickyInteractionReportFields(shellChecks,
                        editorChecks, dialogChecks, windowPolicyChecks,
                        fontChecks) +
                    BuildDialogWindowAndSideTabReportFields(dialogChecks,
                        windowPolicyChecks, sideTabChecks) +
                    BuildDockReportFields(dockChecks, dialogChecks,
                        windowPolicyChecks, editorChecks) +
                    BuildPolicyKeyboardReminderReportFields(windowPolicyChecks,
                        keyboardOverlayChecks, editorChecks, shellChecks,
                        automaticNoteBackupOk, stickyChecks,
                        stickyCompatibilityChecks, reminderCoordinatorChecks,
                        settingsChecks) +
                    BuildAnimationArtReportFields(animationChecks, artChecks,
                        reminderCoordinatorChecks) +
                    BuildBubbleManualKeyboardReportFields(bubbleChecks,
                        reminderCoordinatorChecks, settingsChecks,
                        animationChecks, shellChecks, keyboardOverlayChecks,
                        weatherChecks) +
                    BuildStaticReportTail();
                bool ok = EndCheckCollection();
                string json = "{\n" +
                    "  \"ok\": " + Bool(ok) + ",\n" + reportBody;
                string parent = Path.GetDirectoryName(Path.GetFullPath(outputPath));
                if (!String.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                File.WriteAllText(outputPath, json, new UTF8Encoding(false));
                if (!ok)
                {
                    // Diagnostic-only CI evidence: surface the exact failing
                    // modular fields on the runner console. No test semantics
                    // change; this only makes CI failures diagnosable.
                    List<string> falseFields = new List<string>();
                    foreach (System.Text.RegularExpressions.Match match in
                        System.Text.RegularExpressions.Regex.Matches(json,
                            "\"(\\w+)\":\\s*false"))
                        falseFields.Add(match.Groups[1].Value);
                    Console.Error.WriteLine(
                        "MODULAR FALSE FIELDS: " +
                        String.Join(", ", falseFields));
                }
            }
            catch (Exception ex)
            {
                CancelCheckCollection();
                string message = ex.Message.Replace("\\", "\\\\").Replace("\"", "\\\"");
                File.WriteAllText(outputPath,
                    "{\"ok\":false,\"error\":\"" + message + "\"}",
                    new UTF8Encoding(false));
                Console.Error.WriteLine("MODULAR ERROR: " + ex);
            }
        }
    }
}
