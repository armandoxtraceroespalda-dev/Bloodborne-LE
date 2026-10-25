# BetterKBM engine source integration 36

Upstream: Awakenchan/LanternEngine, d901fbd2ea4bea114b37c26a2a9278d1e97f7b46
(0.9.37, d901fbd2). Since beta 7 the original project stores the full engine source
in its engine directory. This integration follows that layout, retaining the
BetterKBM controls, native menu organization, language overrides, live settings
synchronization and resolution policy. Legacy bbhost-changes.patch and older
better-kbm-*.patch files may remain in an existing checkout; the build does not
apply them.

From MSYS2 CLANG64 in the launcher clone, run:

```sh
bash tools/build-better-kbm.sh
```

The default target is ../BetterKBM-engine-update36. It is a separate source copy,
so the previous engine remains available. No separate base-engine clone or
submodule download is needed: upstream now vendors those source dependencies.
Only missing player settings/assets are migrated from the engine selected in
launcher settings, with the beta 6/expanded folder as fallback. Existing target data are never replaced by migration.
Source hashes are verified before building. Select the printed engine folder
in the launcher after a successful build.

Original project information: [UPSTREAM-beta8.md](UPSTREAM-beta8.md).
Native graphics execution and the full Windows Vulkan build require a Windows
validation environment; portable regression tests do not substitute for that.

Integration notes and validation limits: [UPSTREAM_INTEGRATION_33.md](../docs/UPSTREAM_INTEGRATION_33.md).

Lantern Engine by Awaken (https://github.com/Awakenchan/LanternEngine).
This is a modified BetterKBM edition. See NOTICE for GNU GPL v3 or later
and its additional terms, and ../docs/UPSTREAM_INTEGRATION_36.md for the merge scope.
