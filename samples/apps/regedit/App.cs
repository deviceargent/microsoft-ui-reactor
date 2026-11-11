// ReactorRegedit — A registry editor built with Reactor + WinUI 3.
// Ports the native Win32 regedit.exe to a declarative component model.

using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using ReactorRegedit;
using ReactorRegedit.Components;
using ReactorRegedit.Components.Dialogs;
using ReactorRegedit.Models;
using ReactorRegedit.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.Win32;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Core.Theme;

ReactorApp.Run<RegeditApp>("Registry Editor", width: 1000, height: 600,
    configure: host => CursorBorderRegistration.Register(host.Reconciler));

// ─── Root component ───────────────────────────────────────────────────────────

class RegeditApp : Component
{
    public override Element Render()
    {
        // ── Core state ───────────────────────────────────────────────
        var (currentKeyPath, setCurrentKeyPath) = UseState("");
        var (values, setValues) = UseState<RegistryValueEntry[]>([]);
        var (selectedValueIndex, setSelectedValueIndex) = UseState(-1);
        var (showAddressBar, setShowAddressBar) = UsePersisted("showAddressBar", true, PersistedScope.Window);
        var (showStatusBar, setShowStatusBar) = UsePersisted("showStatusBar", true, PersistedScope.Window);
        var (addressBarText, setAddressBarText) = UseState("");
        var (isLoading, setIsLoading) = UseState(false);

        // Tree state: combined for single-render updates
        var (treeState, setTreeState) = UseState((
            Expanded: new HashSet<string>(),
            Children: new Dictionary<string, RegistryKeyEntry[]>()
        ));

        // Dialog state
        var (activeDialog, setActiveDialog) = UseState(ActiveDialog.None);
        var (editValueName, setEditValueName) = UseState("");
        var (editValueData, setEditValueData) = UseState("");
        var (editNumberBase, setEditNumberBase) = UseState(NumberBase.Hexadecimal);
        var (editValueKind, setEditValueKind) = UseState(RegistryValueKind.String);

        // Find state
        var (searchText, setSearchText) = UseState("");
        var (findFlags, setFindFlags) = UseState(FindFlags.Keys | FindFlags.Values | FindFlags.Data);
        var searchCtsRef = UseRef<CancellationTokenSource?>(null);

        // Favorites state
        var (favorites, setFavorites) = UseState(new Dictionary<string, string>());
        var (favoriteName, setFavoriteName) = UseState("");
        var (selectedFavoriteIndex, setSelectedFavoriteIndex) = UseState(-1);

        // Export state
        var (exportAll, setExportAll) = UseState(false);
        var (exportTarget, setExportTarget) = UseState("");
        var (favoriteTarget, setFavoriteTarget) = UseState("");

        // Rename / destructive-action bookkeeping. Context menus can target a
        // key other than the one currently open, so the target is captured
        // here instead of read from currentKeyPath at confirm time.
        var (renameKeyPath, setRenameKeyPath) = UseState("");
        var (pendingDelete, setPendingDelete) = UseState<DeleteRequest?>(null);

        // Status bar
        var (statusText, setStatusText) = UseState(Strings.Ready);

        // ── Load values on key path change ───────────────────────────
        UseEffect((Action)(() =>
        {
            if (string.IsNullOrEmpty(currentKeyPath)) return;

            setAddressBarText(currentKeyPath);
            setIsLoading(true);
            var syncContext = SynchronizationContext.Current;

            Task.Run(async () =>
            {
                var result = await RegistryService.GetValuesAsync(currentKeyPath);
                syncContext?.Post(_ =>
                {
                    setValues(result);
                    setSelectedValueIndex(-1);
                    setIsLoading(false);
                }, null);
            });
        }), currentKeyPath);

        // ── Handlers ─────────────────────────────────────────────────
        void NavigateToKey(string path)
        {
            setCurrentKeyPath(path);
        }

        void ExpandTreeNode(string path)
        {
            if (treeState.Expanded.Contains(path)) return;

            var syncContext = SynchronizationContext.Current;
            Task.Run(async () =>
            {
                var subkeys = await RegistryService.GetSubKeysAsync(path);
                syncContext?.Post(_ =>
                {
                    var newChildren = new Dictionary<string, RegistryKeyEntry[]>(treeState.Children)
                    {
                        [path] = subkeys
                    };
                    var newExpanded = new HashSet<string>(treeState.Expanded) { path };
                    setTreeState((Expanded: newExpanded, Children: newChildren));
                }, null);
            });
        }

        void RefreshKey(string path)
        {
            if (string.IsNullOrEmpty(path)) return;

            var syncContext = SynchronizationContext.Current;
            setIsLoading(true);

            Task.Run(async () =>
            {
                var newValues = await RegistryService.GetValuesAsync(path);
                // Also refresh subkeys
                var subkeys = await RegistryService.GetSubKeysAsync(path);
                syncContext?.Post(_ =>
                {
                    if (string.Equals(path, currentKeyPath, StringComparison.OrdinalIgnoreCase))
                        setValues(newValues);

                    var newChildren = new Dictionary<string, RegistryKeyEntry[]>(treeState.Children)
                    {
                        [path] = subkeys
                    };
                    setTreeState((Expanded: treeState.Expanded, Children: newChildren));
                    setIsLoading(false);
                }, null);
            });
        }

        void RefreshCurrentKey() => RefreshKey(currentKeyPath);

        void AddressBarSubmit(string path)
        {
            if (!string.IsNullOrEmpty(path))
                NavigateToKey(path);
        }

        // ── New key/value handlers ───────────────────────────────────
        void CreateNewKey(string targetPath)
        {
            var path = string.IsNullOrEmpty(targetPath) ? currentKeyPath : targetPath;
            if (string.IsNullOrEmpty(path)) return;
            var syncContext = SynchronizationContext.Current;
            Task.Run(async () =>
            {
                // Find unique name
                var baseName = "New Key #";
                var idx = 1;
                var name = baseName + idx;
                using var parent = RegistryService.OpenKey(path);
                if (parent is not null)
                {
                    while (parent.OpenSubKey(name) is not null) { idx++; name = baseName + idx; }
                }
                await RegistryService.CreateKeyAsync(path, name);
                syncContext?.Post(_ =>
                {
                    // Creating a child while the target is collapsed would hide
                    // the new key; expand the target so the result is visible.
                    if (!treeState.Expanded.Contains(path))
                        ExpandTreeNode(path);
                    RefreshKey(path);
                    setStatusText($"Created key {name}.");
                }, null);
            });
        }

        void CreateNewValue(RegistryValueKind kind, string targetPath)
        {
            var path = string.IsNullOrEmpty(targetPath) ? currentKeyPath : targetPath;
            if (string.IsNullOrEmpty(path)) return;
            var syncContext = SynchronizationContext.Current;
            Task.Run(async () =>
            {
                var baseName = "New Value #";
                var idx = 1;
                var name = baseName + idx;
                using var key = RegistryService.OpenKey(path);
                if (key is not null)
                {
                    var existing = key.GetValueNames();
                    while (existing.Contains(name)) { idx++; name = baseName + idx; }
                }
                await RegistryService.CreateValueAsync(path, name, kind);
                syncContext?.Post(_ =>
                {
                    if (!string.Equals(path, currentKeyPath, StringComparison.OrdinalIgnoreCase))
                        NavigateToKey(path);
                    else
                        RefreshKey(path);
                    setStatusText($"Created value {name}.");
                }, null);
            });
        }

        // ── Edit value handlers ──────────────────────────────────────
        void OpenEditDialog(RegistryValueEntry value)
        {
            setEditValueName(value.DisplayName);
            setEditValueKind(value.Kind);
            setEditNumberBase(NumberBase.Hexadecimal);

            switch (value.Kind)
            {
                case RegistryValueKind.String:
                case RegistryValueKind.ExpandString:
                    setEditValueData(value.Data?.ToString() ?? "");
                    setActiveDialog(ActiveDialog.EditString);
                    break;
                case RegistryValueKind.MultiString:
                    setEditValueData(string.Join("\r\n", (string[])(value.Data ?? Array.Empty<string>())));
                    setActiveDialog(ActiveDialog.EditMultiString);
                    break;
                case RegistryValueKind.DWord:
                    setEditValueData(((int)(value.Data ?? 0)).ToString("x"));
                    setActiveDialog(ActiveDialog.EditDword);
                    break;
                case RegistryValueKind.QWord:
                    setEditValueData(((long)(value.Data ?? 0L)).ToString("x"));
                    setActiveDialog(ActiveDialog.EditQword);
                    break;
                case RegistryValueKind.Binary:
                default:
                    setEditValueData(EditBinaryDialog.FormatHexDump(value.Data is byte[] b ? b : []));
                    setActiveDialog(ActiveDialog.EditBinary);
                    break;
            }
        }

        void OpenBinaryEditDialog(RegistryValueEntry value)
        {
            setEditValueName(value.DisplayName);
            setEditValueKind(value.Kind);
            byte[] bytes = value.Kind switch
            {
                RegistryValueKind.Binary => (byte[])(value.Data ?? Array.Empty<byte>()),
                RegistryValueKind.DWord => BitConverter.GetBytes((int)(value.Data ?? 0)),
                RegistryValueKind.QWord => BitConverter.GetBytes((long)(value.Data ?? 0L)),
                _ => global::System.Text.Encoding.Unicode.GetBytes(value.Data?.ToString() ?? "")
            };
            setEditValueData(EditBinaryDialog.FormatHexDump(bytes));
            setActiveDialog(ActiveDialog.EditBinary);
        }

        void SaveEditedValue()
        {
            if (string.IsNullOrEmpty(currentKeyPath)) return;

            var valueName = editValueName == "(Default)" ? "" : editValueName;
            var syncContext = SynchronizationContext.Current;

            object? data = null;
            var kind = editValueKind;

            switch (activeDialog)
            {
                case ActiveDialog.EditString:
                    data = editValueData;
                    kind = editValueKind == RegistryValueKind.ExpandString
                        ? RegistryValueKind.ExpandString : RegistryValueKind.String;
                    break;
                case ActiveDialog.EditMultiString:
                    data = editValueData.Split("\r\n");
                    kind = RegistryValueKind.MultiString;
                    break;
                case ActiveDialog.EditDword:
                    if (editNumberBase == NumberBase.Hexadecimal)
                    {
                        if (int.TryParse(editValueData, global::System.Globalization.NumberStyles.HexNumber, null, out var hexVal))
                            data = hexVal;
                    }
                    else
                    {
                        if (int.TryParse(editValueData, out var decVal))
                            data = decVal;
                    }
                    kind = RegistryValueKind.DWord;
                    break;
                case ActiveDialog.EditQword:
                    if (editNumberBase == NumberBase.Hexadecimal)
                    {
                        if (long.TryParse(editValueData, global::System.Globalization.NumberStyles.HexNumber, null, out var hexVal))
                            data = hexVal;
                    }
                    else
                    {
                        if (long.TryParse(editValueData, out var decVal))
                            data = decVal;
                    }
                    kind = RegistryValueKind.QWord;
                    break;
                case ActiveDialog.EditBinary:
                    data = EditBinaryDialog.ParseHexDump(editValueData);
                    kind = RegistryValueKind.Binary;
                    break;
            }

            if (data is not null)
            {
                Task.Run(async () =>
                {
                    await RegistryService.SetValueAsync(currentKeyPath, valueName, data, kind);
                    syncContext?.Post(_ =>
                    {
                        setActiveDialog(ActiveDialog.None);
                        RefreshCurrentKey();
                    }, null);
                });
            }
            else
            {
                setActiveDialog(ActiveDialog.None);
            }
        }

        // ── Delete handlers ──────────────────────────────────────────
        /// <summary>
        /// Requests confirmation for a destructive action. Context menus can
        /// target a key other than the open one, so the request carries both
        /// the path and the label to show in the dialog.
        /// </summary>
        void RequestDelete(string keyPath, string valueName, string label, bool isKey)
        {
            if (string.IsNullOrEmpty(keyPath)) return;
            setPendingDelete(new DeleteRequest(keyPath, valueName, label, isKey));
        }

        void ConfirmPendingDelete()
        {
            if (pendingDelete is not { } request) return;
            setPendingDelete(null);
            var syncContext = SynchronizationContext.Current;

            if (!request.IsKey)
            {
                Task.Run(async () =>
                {
                    await RegistryService.DeleteValueAsync(request.KeyPath, request.ValueName);
                    syncContext?.Post(_ =>
                    {
                        RefreshKey(request.KeyPath);
                        setStatusText($"Deleted value {request.Label}.");
                    }, null);
                });
                return;
            }

            // Root hives cannot be deleted.
            if (!request.KeyPath.Contains('\\')) return;
            var parentPath = request.KeyPath[..request.KeyPath.LastIndexOf('\\')];

            Task.Run(async () =>
            {
                var success = await RegistryService.DeleteKeyAsync(request.KeyPath);
                syncContext?.Post(_ =>
                {
                    if (!success)
                    {
                        setStatusText("Could not delete the key.");
                        return;
                    }

                    setStatusText($"Deleted key {request.Label}.");
                    if (string.Equals(request.KeyPath, currentKeyPath, StringComparison.OrdinalIgnoreCase))
                        NavigateToKey(parentPath);
                    else
                        RefreshKey(parentPath);
                }, null);
            });
        }

        void DeleteSelectedValue(RegistryValueEntry value) =>
            RequestDelete(currentKeyPath, value.Name, value.DisplayName, isKey: false);

        void DeleteKeyPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            var label = path.Contains('\\') ? path[(path.LastIndexOf('\\') + 1)..] : path;
            RequestDelete(path, "", label, isKey: true);
        }

        void DeleteCurrentKey() => DeleteKeyPath(currentKeyPath);

        // ── Rename handlers ─────────────────────────────────────────
        void StartRenameValue(RegistryValueEntry value)
        {
            setEditValueName(value.Name);
            setEditValueData(value.Name);
            setActiveDialog(ActiveDialog.RenameValue);
        }

        void PerformRenameValue()
        {
            if (string.IsNullOrEmpty(currentKeyPath)) return;
            var syncContext = SynchronizationContext.Current;
            var oldName = editValueName;
            var newName = editValueData;
            Task.Run(async () =>
            {
                await RegistryService.RenameValueAsync(currentKeyPath, oldName, newName);
                syncContext?.Post(_ =>
                {
                    setActiveDialog(ActiveDialog.None);
                    RefreshCurrentKey();
                }, null);
            });
        }

        void StartRenameKey()
        {
            if (string.IsNullOrEmpty(currentKeyPath)) return;
            StartRenameKeyAtPath(currentKeyPath);
        }

        void StartRenameKeyAtPath(string path)
        {
            if (string.IsNullOrEmpty(path) || !path.Contains('\\')) return;
            var name = path[(path.LastIndexOf('\\') + 1)..];
            setRenameKeyPath(path);
            setEditValueName(name);
            setEditValueData(name);
            setActiveDialog(ActiveDialog.RenameKey);
        }

        void PerformRenameKey()
        {
            var oldPath = string.IsNullOrEmpty(renameKeyPath) ? currentKeyPath : renameKeyPath;
            if (string.IsNullOrEmpty(oldPath) || !oldPath.Contains('\\')) return;
            var syncContext = SynchronizationContext.Current;
            var newName = editValueData;
            var parentPath = oldPath[..oldPath.LastIndexOf('\\')];
            Task.Run(async () =>
            {
                var success = await RegistryService.RenameKeyAsync(oldPath, newName);
                syncContext?.Post(_ =>
                {
                    setActiveDialog(ActiveDialog.None);
                    if (!success) return;
                    var newPath = $"{parentPath}\\{newName}";
                    if (string.Equals(oldPath, currentKeyPath, StringComparison.OrdinalIgnoreCase))
                        NavigateToKey(newPath);
                    else
                        RefreshKey(parentPath);
                }, null);
            });
        }

        // ── Find handlers ────────────────────────────────────────────
        void PerformFind()
        {
            if (string.IsNullOrEmpty(searchText)) return;
            var startPath = string.IsNullOrEmpty(currentKeyPath) ? "HKEY_LOCAL_MACHINE" : currentKeyPath;
            setActiveDialog(ActiveDialog.None);
            setStatusText($"Searching...");

            searchCtsRef.Current?.Cancel();
            var cts = new CancellationTokenSource();
            searchCtsRef.Current = cts;

            var syncContext = SynchronizationContext.Current;
            Task.Run(async () =>
            {
                try
                {
                    var result = await SearchService.FindNextAsync(startPath, null, searchText, findFlags, cts.Token);
                    syncContext?.Post(_ =>
                    {
                        // Cancellation alone does not make this callback safe: the
                        // search can finish just before a newer one calls Cancel,
                        // in which case nothing throws and this stale result would
                        // navigate the tree and overwrite the newer search's status.
                        // Identity is the check that actually holds -- only the
                        // search that is still current may touch the UI.
                        if (!ReferenceEquals(searchCtsRef.Current, cts)) return;

                        if (result is not null)
                        {
                            NavigateToKey(result.KeyPath);
                            setStatusText(Strings.Ready);
                        }
                        else
                        {
                            setStatusText("Finished searching through the registry.");
                        }
                    }, null);
                }
                // Expected: starting another search cancels the running one (the
                // only cancellation path here -- there is no unmount cleanup).
                // The status text now belongs to that newer search, so writing
                // to it here would clobber the newer search's message.
                catch (OperationCanceledException) { return; }
            });
        }

        void FindNext()
        {
            if (string.IsNullOrEmpty(searchText)) { setActiveDialog(ActiveDialog.Find); return; }
            PerformFind();
        }

        // ── Copy handlers ────────────────────────────────────────────
        static void CopyToClipboard(string text)
        {
            var dataPackage = new global::Windows.ApplicationModel.DataTransfer.DataPackage();
            dataPackage.SetText(text);
            global::Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
        }

        void CopyKeyName(string path)
        {
            var target = string.IsNullOrEmpty(path) ? currentKeyPath : path;
            if (string.IsNullOrEmpty(target)) return;
            CopyToClipboard(target);
            setStatusText("Copied key name.");
        }

        void CopyValueName(RegistryValueEntry value)
        {
            CopyToClipboard(value.DisplayName);
            setStatusText("Copied value name.");
        }

        void CopyValueData(RegistryValueEntry value)
        {
            CopyToClipboard(value.DisplayData);
            setStatusText("Copied value data.");
        }

        // ── Export handler ───────────────────────────────────────────
        void PerformExport()
        {
            var exportPath = exportAll
                ? "HKEY_LOCAL_MACHINE"
                : (string.IsNullOrEmpty(exportTarget) ? currentKeyPath : exportTarget);
            if (string.IsNullOrEmpty(exportPath)) return;

            var syncContext = SynchronizationContext.Current;
            setActiveDialog(ActiveDialog.None);
            setStatusText("Exporting...");

            Task.Run(async () =>
            {
                var content = await RegFileService.ExportAsync(exportPath);
                syncContext?.Post(_ =>
                {
                    CopyToClipboard(content);
                    setStatusText("Exported to clipboard.");
                }, null);
            });
        }

        void StartExport(string path)
        {
            setExportTarget(string.IsNullOrEmpty(path) ? currentKeyPath : path);
            setExportAll(false);
            setActiveDialog(ActiveDialog.Export);
        }

        // ── Favorites handlers ───────────────────────────────────────
        void StartAddFavorite(string path)
        {
            var target = string.IsNullOrEmpty(path) ? currentKeyPath : path;
            if (string.IsNullOrEmpty(target)) return;

            // Default favorite name to the last segment of the key path.
            var name = target.Contains('\\')
                ? target[(target.LastIndexOf('\\') + 1)..]
                : target;
            setFavoriteTarget(target);
            setFavoriteName(name);
            setActiveDialog(ActiveDialog.AddFavorite);
        }

        void AddFavorite()
        {
            var path = string.IsNullOrEmpty(favoriteTarget) ? currentKeyPath : favoriteTarget;
            if (string.IsNullOrEmpty(path)) return;
            var newFavs = new Dictionary<string, string>(favorites)
            {
                [favoriteName] = path
            };
            setFavorites(newFavs);
            setFavoriteTarget("");
            setActiveDialog(ActiveDialog.None);
        }

        void RemoveSelectedFavorite()
        {
            var keys = favorites.Keys.ToArray();
            if (selectedFavoriteIndex < 0 || selectedFavoriteIndex >= keys.Length) return;
            var newFavs = new Dictionary<string, string>(favorites);
            newFavs.Remove(keys[selectedFavoriteIndex]);
            setFavorites(newFavs);
            setActiveDialog(ActiveDialog.None);
        }

        // ── Key context-menu dispatcher ──────────────────────────────
        /// <summary>
        /// The tree's context menu reports the node it was opened on plus a
        /// semantic action; routing happens here so the tree stays presentation
        /// only and every command reuses the menu-bar handlers.
        /// </summary>
        void HandleKeyAction(string path, KeyAction action)
        {
            switch (action)
            {
                case KeyAction.Open:
                    NavigateToKey(path);
                    break;
                case KeyAction.NewKey:
                    CreateNewKey(path);
                    break;
                case KeyAction.NewStringValue:
                    CreateNewValue(RegistryValueKind.String, path);
                    break;
                case KeyAction.NewBinaryValue:
                    CreateNewValue(RegistryValueKind.Binary, path);
                    break;
                case KeyAction.NewDwordValue:
                    CreateNewValue(RegistryValueKind.DWord, path);
                    break;
                case KeyAction.NewQwordValue:
                    CreateNewValue(RegistryValueKind.QWord, path);
                    break;
                case KeyAction.NewMultiStringValue:
                    CreateNewValue(RegistryValueKind.MultiString, path);
                    break;
                case KeyAction.NewExpandStringValue:
                    CreateNewValue(RegistryValueKind.ExpandString, path);
                    break;
                case KeyAction.Rename:
                    StartRenameKeyAtPath(path);
                    break;
                case KeyAction.Delete:
                    DeleteKeyPath(path);
                    break;
                case KeyAction.CopyKeyName:
                    CopyKeyName(path);
                    break;
                case KeyAction.Export:
                    StartExport(path);
                    break;
                case KeyAction.Refresh:
                    RefreshKey(path);
                    break;
                case KeyAction.Find:
                    setActiveDialog(ActiveDialog.Find);
                    break;
                case KeyAction.AddFavorite:
                    StartAddFavorite(path);
                    break;
            }
        }

        // ── Permissions (P/Invoke native security dialog) ────────────
        void ShowPermissions()
        {
            // Out of scope for initial implementation — would require P/Invoke to aclui.dll EditSecurity
        }

        // ── Number base change handler ───────────────────────────────
        void OnDwordBaseChanged(NumberBase newBase)
        {
            // Convert display value between hex and decimal
            if (activeDialog == ActiveDialog.EditDword)
            {
                if (newBase == NumberBase.Decimal && editNumberBase == NumberBase.Hexadecimal)
                {
                    if (int.TryParse(editValueData, global::System.Globalization.NumberStyles.HexNumber, null, out var val))
                        setEditValueData(val.ToString());
                }
                else if (newBase == NumberBase.Hexadecimal && editNumberBase == NumberBase.Decimal)
                {
                    if (int.TryParse(editValueData, out var val))
                        setEditValueData(val.ToString("x"));
                }
            }
            else if (activeDialog == ActiveDialog.EditQword)
            {
                if (newBase == NumberBase.Decimal && editNumberBase == NumberBase.Hexadecimal)
                {
                    if (long.TryParse(editValueData, global::System.Globalization.NumberStyles.HexNumber, null, out var val))
                        setEditValueData(val.ToString());
                }
                else if (newBase == NumberBase.Hexadecimal && editNumberBase == NumberBase.Decimal)
                {
                    if (long.TryParse(editValueData, out var val))
                        setEditValueData(val.ToString("x"));
                }
            }
            setEditNumberBase(newBase);
        }

        // ── Build menu bar ───────────────────────────────────────────
        var menuBar = MenuBar([
            // File
            Menu(Strings.FileMenu, [
                MenuItem(Strings.Import, () =>
                {
                    // For now, import from clipboard
                    var syncContext = SynchronizationContext.Current;
                    Task.Run(async () =>
                    {
                        try
                        {
                            var content = await global::Windows.ApplicationModel.DataTransfer.Clipboard.GetContent().GetTextAsync();
                            if (!string.IsNullOrEmpty(content))
                            {
                                var count = await RegFileService.ImportAsync(content);
                                syncContext?.Post(_ =>
                                {
                                    setStatusText($"Imported {count} key(s).");
                                    RefreshCurrentKey();
                                }, null);
                            }
                        }
                        catch { }
                    });
                }),
                MenuItem(Strings.Export, () => StartExport(currentKeyPath)) with
                    { KeyboardAccelerators = [Accelerator(global::Windows.System.VirtualKey.E, global::Windows.System.VirtualKeyModifiers.Control)] },
                MenuSeparator(),
                MenuItem(Strings.LoadHive, () => { /* requires elevation */ }),
                MenuItem(Strings.UnloadHive, () => { /* requires elevation */ }),
                MenuSeparator(),
                MenuItem(Strings.ConnectNetworkRegistry, () => { }),
                MenuItem(Strings.DisconnectNetworkRegistry, () => { }),
                MenuSeparator(),
                MenuItem(Strings.Print, () => { }) with
                    { KeyboardAccelerators = [Accelerator(global::Windows.System.VirtualKey.P, global::Windows.System.VirtualKeyModifiers.Control)] },
                MenuSeparator(),
                MenuItem(Strings.Exit, () => Application.Current.Exit()),
            ]),

            // Edit
            Menu(Strings.EditMenu, [
                MenuSubItem(Strings.NewMenu, [
                    MenuItem(Strings.NewKey, () => CreateNewKey(currentKeyPath)),
                    MenuSeparator(),
                    MenuItem(Strings.NewStringValue, () => CreateNewValue(RegistryValueKind.String, currentKeyPath)),
                    MenuItem(Strings.NewBinaryValue, () => CreateNewValue(RegistryValueKind.Binary, currentKeyPath)),
                    MenuItem(Strings.NewDwordValue, () => CreateNewValue(RegistryValueKind.DWord, currentKeyPath)),
                    MenuItem(Strings.NewQwordValue, () => CreateNewValue(RegistryValueKind.QWord, currentKeyPath)),
                    MenuItem(Strings.NewMultiStringValue, () => CreateNewValue(RegistryValueKind.MultiString, currentKeyPath)),
                    MenuItem(Strings.NewExpandableStringValue, () => CreateNewValue(RegistryValueKind.ExpandString, currentKeyPath)),
                ]),
                MenuSeparator(),
                MenuItem(Strings.Permissions, ShowPermissions),
                MenuSeparator(),
                MenuItem(Strings.Delete, DeleteCurrentKey) with
                    { KeyboardAccelerators = [Accelerator(global::Windows.System.VirtualKey.Delete)] },
                MenuItem(Strings.Rename, StartRenameKey) with
                    { KeyboardAccelerators = [Accelerator(global::Windows.System.VirtualKey.F2)] },
                MenuSeparator(),
                MenuItem(Strings.CopyKeyName, () => CopyKeyName(currentKeyPath)),
                MenuSeparator(),
                MenuItem(Strings.Find, () => setActiveDialog(ActiveDialog.Find)) with
                    { KeyboardAccelerators = [Accelerator(global::Windows.System.VirtualKey.F, global::Windows.System.VirtualKeyModifiers.Control)] },
                MenuItem(Strings.FindNext, FindNext) with
                    { KeyboardAccelerators = [Accelerator(global::Windows.System.VirtualKey.F3)] },
            ]),

            // View
            Menu(Strings.ViewMenu, [
                ToggleMenuItem(Strings.AddressBar, showAddressBar, _ => setShowAddressBar(!showAddressBar)),
                ToggleMenuItem(Strings.StatusBar, showStatusBar, _ => setShowStatusBar(!showStatusBar)),
                MenuSeparator(),
                MenuItem(Strings.Split, () => { }),
                MenuSeparator(),
                MenuItem(Strings.Refresh, RefreshCurrentKey) with
                    { KeyboardAccelerators = [Accelerator(global::Windows.System.VirtualKey.F5)] },
            ]),

            // Favorites
            Menu(Strings.FavoritesMenu, BuildFavoritesMenu(favorites, currentKeyPath,
                StartAddFavorite, setActiveDialog, setSelectedFavoriteIndex, NavigateToKey)),

            // Help
            Menu(Strings.HelpMenu, [
                MenuItem(Strings.About, () => setActiveDialog(ActiveDialog.About)),
            ]),
        ]);

        // ── Address bar ──────────────────────────────────────────────
        var addressBar = When(showAddressBar, () =>
            Border(
                HStack(10,
                    TextBlock("\uE8B7")
                        .FontFamily(Palette.IconFont)
                        .FontSize(14)
                        .Foreground(Palette.Blue)
                        .VAlign(VerticalAlignment.Center),
                    TextBlock(Strings.Computer)
                        .FontSize(10)
                        .SemiBold()
                        .Foreground(Palette.TextMuted)
                        .VAlign(VerticalAlignment.Center)
                        .Set(tb => tb.CharacterSpacing = 60),
                    AutoSuggestBox(addressBarText, setAddressBarText, text => AddressBarSubmit(text))
                        .PlaceholderText("HKEY_LOCAL_MACHINE\\SOFTWARE")
                        .HAlign(HorizontalAlignment.Stretch)
                ).Padding(12, 6, 12, 6)
            )
            .Background(Palette.BackgroundAlt)

        .WithBorder(Palette.BorderSoft)
        );

        // ── Tree pane ────────────────────────────────────────────────
        var tree = Component<RegistryTree, RegistryTreeProps>(new RegistryTreeProps(
            ExpandedPaths: treeState.Expanded,
            TreeChildren: treeState.Children,
            CurrentKeyPath: currentKeyPath,
            OnNavigate: NavigateToKey,
            OnExpand: ExpandTreeNode,
            OnKeyAction: HandleKeyAction
        ));

        // ── Value list pane ──────────────────────────────────────────
        var valueList = Component<ValueList, ValueListProps>(new ValueListProps(
            KeyPath: currentKeyPath,
            Values: values,
            SelectedIndex: selectedValueIndex,
            OnSelectionChanged: setSelectedValueIndex,
            OnModify: OpenEditDialog,
            OnModifyBinary: OpenBinaryEditDialog,
            OnDelete: DeleteSelectedValue,
            OnRename: StartRenameValue,
            OnCopyName: CopyValueName,
            OnCopyData: CopyValueData,
            OnNewKey: () => CreateNewKey(currentKeyPath),
            OnNewValue: kind => CreateNewValue(kind, currentKeyPath),
            OnRefresh: RefreshCurrentKey,
            OnExport: () => StartExport(currentKeyPath),
            OnCopyKeyPath: () => CopyKeyName(currentKeyPath),
            OnFind: () => setActiveDialog(ActiveDialog.Find)
        ));

        // ── Status bar ───────────────────────────────────────────────
        var statusBar = When(showStatusBar, () =>
            Grid(
                [GridSize.Auto, GridSize.Star(), GridSize.Auto],
                [GridSize.Px(28)],
                HStack(8,
                    DotEl(isLoading ? Palette.Amber : Palette.Mint),
                    TextBlock(statusText)
                        .FontSize(11)
                        .Foreground(Palette.TextSecondary)
                        .TextTrimming(Microsoft.UI.Xaml.TextTrimming.CharacterEllipsis)
                        .TextWrapping(Microsoft.UI.Xaml.TextWrapping.NoWrap)
                        .VAlign(VerticalAlignment.Center)
                ).Padding(12, 0, 8, 0).Grid(row: 0, column: 0),
                TextBlock(string.IsNullOrEmpty(currentKeyPath) ? Strings.Computer : currentKeyPath)
                    .FontFamily(Palette.MonoFont)
                    .FontSize(10.5)
                    .Foreground(Palette.TextMuted)
                    .TextTrimming(Microsoft.UI.Xaml.TextTrimming.CharacterEllipsis)
                    .TextWrapping(Microsoft.UI.Xaml.TextWrapping.NoWrap)
                    .HAlign(HorizontalAlignment.Right)
                    .VAlign(VerticalAlignment.Center)
                    .Margin(8, 0, 12, 0)
                    .Grid(row: 0, column: 1),
                When(isLoading,
                        () => ProgressRing().Width(14).Height(14).IsActive(true))
                    .Margin(0, 0, 12, 0)
                    .Grid(row: 0, column: 2)
            )
            .Background(Palette.BackgroundAlt)

        .WithBorder(Palette.BorderSoft)
        );

        // ── Dialogs ─────────────────────────────────────────────────
        var editStringDialog = Component<EditStringDialog, EditStringDialogProps>(new EditStringDialogProps(
            IsOpen: activeDialog == ActiveDialog.EditString,
            Title: editValueKind == RegistryValueKind.ExpandString ? "Edit Expandable String" : Strings.EditStringTitle,
            ValueName: editValueName,
            ValueData: editValueData,
            OnValueDataChanged: setEditValueData,
            OnSave: SaveEditedValue,
            OnCancel: () => setActiveDialog(ActiveDialog.None)
        ));

        var editMultiStringDialog = Component<EditMultiStringDialog, EditMultiStringDialogProps>(new EditMultiStringDialogProps(
            IsOpen: activeDialog == ActiveDialog.EditMultiString,
            ValueName: editValueName,
            ValueData: editValueData,
            OnValueDataChanged: setEditValueData,
            OnSave: SaveEditedValue,
            OnCancel: () => setActiveDialog(ActiveDialog.None)
        ));

        var editDwordDialog = Component<EditDwordDialog, EditDwordDialogProps>(new EditDwordDialogProps(
            IsOpen: activeDialog == ActiveDialog.EditDword,
            ValueName: editValueName,
            ValueData: editValueData,
            NumberBase: editNumberBase,
            OnValueDataChanged: setEditValueData,
            OnBaseChanged: OnDwordBaseChanged,
            OnSave: SaveEditedValue,
            OnCancel: () => setActiveDialog(ActiveDialog.None)
        ));

        var editQwordDialog = Component<EditQwordDialog, EditQwordDialogProps>(new EditQwordDialogProps(
            IsOpen: activeDialog == ActiveDialog.EditQword,
            ValueName: editValueName,
            ValueData: editValueData,
            NumberBase: editNumberBase,
            OnValueDataChanged: setEditValueData,
            OnBaseChanged: OnDwordBaseChanged,
            OnSave: SaveEditedValue,
            OnCancel: () => setActiveDialog(ActiveDialog.None)
        ));

        var editBinaryDialog = Component<EditBinaryDialog, EditBinaryDialogProps>(new EditBinaryDialogProps(
            IsOpen: activeDialog == ActiveDialog.EditBinary,
            ValueName: editValueName,
            ValueData: editValueData,
            OnValueDataChanged: setEditValueData,
            OnSave: SaveEditedValue,
            OnCancel: () => setActiveDialog(ActiveDialog.None)
        ));

        var findDialog = Component<FindDialog, FindDialogProps>(new FindDialogProps(
            IsOpen: activeDialog == ActiveDialog.Find,
            SearchText: searchText,
            Flags: findFlags,
            OnSearchTextChanged: setSearchText,
            OnFlagsChanged: setFindFlags,
            OnFind: PerformFind,
            OnCancel: () => setActiveDialog(ActiveDialog.None)
        ));

        var addFavoriteDialog = Component<AddFavoriteDialog, AddFavoriteDialogProps>(new AddFavoriteDialogProps(
            IsOpen: activeDialog == ActiveDialog.AddFavorite,
            FavoriteName: favoriteName,
            OnNameChanged: setFavoriteName,
            OnSave: AddFavorite,
            OnCancel: () => setActiveDialog(ActiveDialog.None)
        ));

        var removeFavoriteDialog = Component<RemoveFavoriteDialog, RemoveFavoriteDialogProps>(new RemoveFavoriteDialogProps(
            IsOpen: activeDialog == ActiveDialog.RemoveFavorite,
            FavoriteNames: favorites.Keys.ToArray(),
            SelectedIndex: selectedFavoriteIndex,
            OnSelectionChanged: setSelectedFavoriteIndex,
            OnRemove: RemoveSelectedFavorite,
            OnCancel: () => setActiveDialog(ActiveDialog.None)
        ));

        var exportDialog = Component<ExportDialog, ExportDialogProps>(new ExportDialogProps(
            IsOpen: activeDialog == ActiveDialog.Export,
            ExportAll: exportAll,
            SelectedBranch: string.IsNullOrEmpty(exportTarget) ? currentKeyPath : exportTarget,
            OnExportAllChanged: setExportAll,
            OnExport: PerformExport,
            OnCancel: () => setActiveDialog(ActiveDialog.None)
        ));

        var renameKeyDialog = ContentDialog(
            Strings.RenameKeyTitle,
            VStack(8,
                TextBlock(Strings.NewKeyName),
                TextBox(editValueData, setEditValueData)
            ).Width(350),
            Strings.OK
        ) with
        {
            IsOpen = activeDialog == ActiveDialog.RenameKey,
            SecondaryButtonText = Strings.Cancel,
            DefaultButton = Microsoft.UI.Xaml.Controls.ContentDialogButton.Primary,
            OnClosed = result =>
            {
                if (result == Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary)
                    PerformRenameKey();
                else
                    setActiveDialog(ActiveDialog.None);
            },
        };

        var renameValueDialog = ContentDialog(
            Strings.RenameValueTitle,
            VStack(8,
                TextBlock(Strings.NewKeyName),
                TextBox(editValueData, setEditValueData)
            ).Width(350),
            Strings.OK
        ) with
        {
            IsOpen = activeDialog == ActiveDialog.RenameValue,
            SecondaryButtonText = Strings.Cancel,
            DefaultButton = Microsoft.UI.Xaml.Controls.ContentDialogButton.Primary,
            OnClosed = result =>
            {
                if (result == Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary)
                    PerformRenameValue();
                else
                    setActiveDialog(ActiveDialog.None);
            },
        };

        var aboutDialog = ContentDialog(
            Strings.AboutTitle,
            TextBlock(Strings.AboutText),
            Strings.OK
        ) with
        {
            IsOpen = activeDialog == ActiveDialog.About,
            OnClosed = _ => setActiveDialog(ActiveDialog.None),
        };

        // ── Destructive-action confirmation ─────────────────────────
        // Context menus make deletion a single misclick away, so every
        // destructive command is routed through this dialog.
        var confirmDeleteDialog = (ContentDialog(
            Strings.ConfirmTitle,
            VStack(10,
                TextBlock(pendingDelete?.IsKey == true
                        ? Strings.ConfirmDeleteKey
                        : Strings.ConfirmDeleteValue)
                    .TextWrapping(TextWrapping.Wrap)
                    .Foreground(Palette.TextSecondary),
                TextBlock(pendingDelete is null
                        ? ""
                        : (pendingDelete.IsKey ? "KEY" : "VALUE") + "\n" + pendingDelete.Label)
                    .FontFamily(Palette.MonoFont)
                    .FontSize(12)
                    .Foreground(pendingDelete?.IsKey == true ? Palette.Amber : Palette.Mint)
            ).Width(420),
            Strings.Delete
        ) with
        {
            IsOpen = pendingDelete is not null,
            CloseButtonText = Strings.Cancel,
            DefaultButton = Microsoft.UI.Xaml.Controls.ContentDialogButton.Close,
            OnClosed = result =>
            {
                if (result == Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary)
                    ConfirmPendingDelete();
                else
                    setPendingDelete(null);
            },
        }).Set(d => d.RequestedTheme = ElementTheme.Dark);

        // ── Layout ───────────────────────────────────────────────────
        var menuBarElement = menuBar.Set(mb =>
        {
            mb.Background = BrushHelper.Parse(Palette.BackgroundAlt);
            mb.Padding = new Thickness(6, 0, 6, 0);
        });

        return Grid(
            [GridSize.Star()],
            [
                GridSize.Auto, // brand header
                GridSize.Auto, // accent strip
                GridSize.Auto, // menu bar
                GridSize.Auto, // address bar
                GridSize.Star(), // tree + values
                GridSize.Auto, // status bar
            ],
            BuildHeader().Grid(row: 0, column: 0),
            BuildAccentStrip().Grid(row: 1, column: 0),
            menuBarElement.Grid(row: 2, column: 0),
            addressBar.Grid(row: 3, column: 0),
            Component<SplitPanel, SplitPanelProps>(new SplitPanelProps(
                Left: tree,
                Right: VStack(0,
                    valueList,
                    // Dialogs rendered here so they're part of the visual tree
                    editStringDialog,
                    editMultiStringDialog,
                    editDwordDialog,
                    editQwordDialog,
                    editBinaryDialog,
                    findDialog,
                    addFavoriteDialog,
                    removeFavoriteDialog,
                    exportDialog,
                    renameKeyDialog,
                    renameValueDialog,
                    aboutDialog,
                    confirmDeleteDialog
                )
            )).Grid(row: 4, column: 0),
            statusBar.Grid(row: 5, column: 0)
        )
        .Background(Palette.Background)
        .RequestedTheme(ElementTheme.Dark);
    }

    // ── Shell chrome helpers ─────────────────────────────────────────

    /// <summary>Brand header: three accent dots, the app title, and a chip.</summary>
    private static Element BuildHeader() =>
        Grid(
            [GridSize.Star(), GridSize.Auto],
            [GridSize.Auto],
            HStack(12,
                HStack(4,
                    DotEl(Palette.Blue),
                    DotEl(Palette.Purple),
                    DotEl(Palette.Amber)
                ).VAlign(VerticalAlignment.Center),
                VStack(1,
                    TextBlock("Registry Editor")
                        .FontSize(15)
                        .SemiBold()
                        .Foreground(Palette.TextPrimary),
                    TextBlock("Microsoft.UI.Reactor  ·  WinUI 3")
                        .FontSize(10)
                        .Foreground(Palette.TextMuted)
                )
            ).Grid(row: 0, column: 0).VAlign(VerticalAlignment.Center),
            ChipEl(Strings.ReactorBadge, Palette.Purple)
                .Grid(row: 0, column: 1)
                .VAlign(VerticalAlignment.Center)
        )
        .Background(Palette.BackgroundAlt)
        .Padding(16, 10, 16, 10);

    /// <summary>Thin blue → purple → pink → amber gradient rule under the header.</summary>
    private static Element BuildAccentStrip()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0.5),
            EndPoint = new Windows.Foundation.Point(1, 0.5),
        };

        foreach (var (offset, color) in new[]
        {
            (0.00, Palette.Blue),
            (0.35, Palette.Purple),
            (0.70, Palette.Pink),
            (1.00, Palette.Amber),
        })
        {
            brush.GradientStops.Add(new GradientStop
            {
                Offset = offset,
                Color = BrushHelper.Parse(color).Color,
            });
        }

        return Border(Empty())
            .Height(2)
            .Background(brush)
            .HAlign(HorizontalAlignment.Stretch);
    }

    private static Element DotEl(string color) =>
        Border(Empty())
            .Width(6)
            .Height(6)
            .CornerRadius(3)
            .Background(color)
            .VAlign(VerticalAlignment.Center);

    private static Element ChipEl(string text, string color) =>
        Border(
            TextBlock(text)
                .FontSize(10)
                .SemiBold()
                .Foreground(color)
                .Set(tb => tb.CharacterSpacing = 140)
        )
        .Background(Palette.Tint(color, 0x22))
        .BorderBrush(Palette.Tint(color, 0x66))
        .BorderThickness(1)
        .CornerRadius(4)
        .Padding(10, 3, 10, 3);

    /// <summary>
    /// Builds the dynamic Favorites menu items (Add/Remove + saved favorites).
    /// </summary>
    private static MenuFlyoutItemBase[] BuildFavoritesMenu(
        Dictionary<string, string> favorites,
        string currentKeyPath,
        Action<string> startAddFavorite,
        Action<ActiveDialog> setActiveDialog,
        Action<int> setSelectedFavoriteIndex,
        Action<string> navigateToKey)
    {
        var items = new List<MenuFlyoutItemBase>
        {
            MenuItem(Strings.AddToFavorites, () => startAddFavorite(currentKeyPath)),
            MenuItem(Strings.RemoveFromFavorites, () =>
            {
                setSelectedFavoriteIndex(-1);
                setActiveDialog(ActiveDialog.RemoveFavorite);
            }),
        };

        if (favorites.Count > 0)
        {
            items.Add(MenuSeparator());
            foreach (var (name, path) in favorites)
            {
                items.Add(MenuItem(name, () => navigateToKey(path)));
            }
        }

        return items.ToArray();
    }

}
