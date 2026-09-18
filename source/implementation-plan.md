# System icon coverage and interaction update

User requirements: remove the old policy feature; add double-click and context menus; make selection prominent; hide every icon category in Windhawk Taskbar tray system icon tweaks 1.3. Keep sources in source/.

- Remove SystemIconPolicies.cs, its dialog, recovery diagnostics and obsolete translations/documentation. Do not mutate existing user settings during removal.
- Add one C# icon catalog with masks 1..2048; extend the native classifier and scoped traversal to microphone, location, Studio Effects, Recall, language, language supplementary icons, bell and Show desktop. Preserve clock support. Shared microphone/location requires both requests. Restore stale targets when their content changes.
- Use the catalog in the live dialog and diagnostics; serialize UI actions, route buttons/double-click/context menu through one handler, allow waiting requests for absent icons and disable interaction during operations.
- Add persistent blue selection, white foreground and selection borders/markers for list and grid in both themes.
- Build; run native classifier checks, actual detected-icon hide/restore/crash recovery, and dialog interaction tests. Inspect Chinese/English dialog and selected-item screenshots. Publish a complete package with native source/license.

Only hiding is requested; battery grayscale, custom desktop-button width and conditional bell modes are not part of this change. Real-world checks can only cover controls present on the current desktop; report skips explicitly.

Completed: release build and self-contained x64 publish succeeded; all 11 UI/selection checks and native glyph checks passed. Volume, network, clock, both language categories and Show desktop passed individual hiding/restoration, combined hiding, session-close restoration and owner-crash restoration. Battery, microphone, location, Studio Effects, Recall and notification bell were absent and skipped for live checks. Chinese and English dialogs and light/dark selection screenshots were inspected. Published package includes native source and GPL license.
