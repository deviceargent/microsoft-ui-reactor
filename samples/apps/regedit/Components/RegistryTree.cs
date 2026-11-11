using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using ReactorRegedit.Models;
using ReactorRegedit.Services;
using static Microsoft.UI.Reactor.Factories;

namespace ReactorRegedit.Components;

internal sealed record RegistryTreeProps(
    HashSet<string> ExpandedPaths,
    Dictionary<string, RegistryKeyEntry[]> TreeChildren,
    string CurrentKeyPath,
    Action<string> OnNavigate,
    Action<string> OnExpand,
    Action<string, KeyAction> OnKeyAction
);

internal sealed class RegistryTree : Component<RegistryTreeProps>
{
    public override Element Render()
    {
        var pathMap = UseRef(new Dictionary<string, string>());
        var rightTapped = UseRef<RightTappedEventHandler?>(null);

        var nodes = UseMemo(() =>
        {
            pathMap.Current.Clear();
            return BuildNodes(pathMap.Current, Props.ExpandedPaths, Props.TreeChildren);
        }, Props.ExpandedPaths.Count, Props.TreeChildren.Count);

        return TreeView(nodes)
            .Set(tv =>
            {
                tv.SelectionMode = TreeViewSelectionMode.Single;
                tv.Background = BrushHelper.Parse("transparent");
                tv.Foreground = BrushHelper.Parse(Palette.TextPrimary);
                tv.Padding = new Thickness(6, 4, 2, 4);

                // Context menu target depends on the node under the pointer, so
                // the flyout is built at right-tap time instead of declaratively.
                if (rightTapped.Current is not null)
                    tv.RemoveHandler(UIElement.RightTappedEvent, rightTapped.Current);

                var handler = new RightTappedEventHandler((s, e) => ShowContextMenu(tv, e, Props));
                rightTapped.Current = handler;
                tv.AddHandler(UIElement.RightTappedEvent, handler, handledEventsToo: true);
            }) with
            {
                OnItemInvoked = node =>
                {
                    if (pathMap.Current.TryGetValue(node.Content, out var fullPath))
                        Props.OnNavigate(fullPath);
                },
                OnExpanding = node =>
                {
                    if (pathMap.Current.TryGetValue(node.Content, out var fullPath))
                    {
                        if (!Props.ExpandedPaths.Contains(fullPath))
                            Props.OnExpand(fullPath);
                    }
                }
            };
    }

    // ── Context menu ─────────────────────────────────────────────────

    private static void ShowContextMenu(TreeView tree, RightTappedRoutedEventArgs e, RegistryTreeProps props)
    {
        var container = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        var path = container is null ? null : PathFromNode(tree.NodeFromContainer(container));

        var menu = path is not null
            ? BuildKeyMenu(path, props)
            : BuildBackgroundMenu(props.CurrentKeyPath, props);

        if (container is not null)
            menu.ShowAt(container, new FlyoutShowOptions { Position = e.GetPosition(container) });
        else
            menu.ShowAt(tree, new FlyoutShowOptions { Position = e.GetPosition(tree) });

        e.Handled = true;
    }

    private static MenuFlyout BuildKeyMenu(string path, RegistryTreeProps props)
    {
        var menu = new MenuFlyout();
        menu.Items.Add(Item(Strings.Open, "\uE8E5", () => props.OnKeyAction(path, KeyAction.Open)));
        menu.Items.Add(NewSubMenu(path, props));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(Item(Strings.Rename, "\uE8AC", () => props.OnKeyAction(path, KeyAction.Rename)));
        menu.Items.Add(Item(Strings.Delete, "\uE74D", () => props.OnKeyAction(path, KeyAction.Delete)));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(Item(Strings.CopyKeyName, "\uE8C8", () => props.OnKeyAction(path, KeyAction.CopyKeyName)));
        menu.Items.Add(Item(Strings.Export, "\uE74E", () => props.OnKeyAction(path, KeyAction.Export)));
        menu.Items.Add(Item(Strings.AddToFavorites, "\uE734", () => props.OnKeyAction(path, KeyAction.AddFavorite)));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(Item(Strings.Find, "\uE721", () => props.OnKeyAction(path, KeyAction.Find)));
        menu.Items.Add(Item(Strings.Refresh, "\uE72C", () => props.OnKeyAction(path, KeyAction.Refresh)));
        return menu;
    }

    private static MenuFlyout BuildBackgroundMenu(string path, RegistryTreeProps props)
    {
        var hasKey = !string.IsNullOrEmpty(path);
        var menu = new MenuFlyout();
        menu.Items.Add(NewSubMenu(path, props));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(Item(Strings.Refresh, "\uE72C", () => props.OnKeyAction(path, KeyAction.Refresh), hasKey));
        menu.Items.Add(Item(Strings.Find, "\uE721", () => props.OnKeyAction(path, KeyAction.Find), hasKey));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(Item(Strings.Export, "\uE74E", () => props.OnKeyAction(path, KeyAction.Export), hasKey));
        menu.Items.Add(Item(Strings.CopyKeyName, "\uE8C8", () => props.OnKeyAction(path, KeyAction.CopyKeyName), hasKey));
        menu.Items.Add(Item(Strings.AddToFavorites, "\uE734", () => props.OnKeyAction(path, KeyAction.AddFavorite), hasKey));
        return menu;
    }

    private static MenuFlyoutSubItem NewSubMenu(string path, RegistryTreeProps props)
    {
        var enabled = !string.IsNullOrEmpty(path);
        var sub = new MenuFlyoutSubItem
        {
            Text = Strings.NewMenu,
            IsEnabled = enabled,
            Icon = Icon("\uE710"),
        };
        sub.Items.Add(Item(Strings.NewKey, "\uE8B7", () => props.OnKeyAction(path, KeyAction.NewKey)));
        sub.Items.Add(new MenuFlyoutSeparator());
        sub.Items.Add(Item(Strings.NewStringValue, "\uE8A5", () => props.OnKeyAction(path, KeyAction.NewStringValue)));
        sub.Items.Add(Item(Strings.NewBinaryValue, "\uE9F5", () => props.OnKeyAction(path, KeyAction.NewBinaryValue)));
        sub.Items.Add(Item(Strings.NewDwordValue, "\uE8EF", () => props.OnKeyAction(path, KeyAction.NewDwordValue)));
        sub.Items.Add(Item(Strings.NewQwordValue, "\uE8EF", () => props.OnKeyAction(path, KeyAction.NewQwordValue)));
        sub.Items.Add(Item(Strings.NewMultiStringValue, "\uE8FD", () => props.OnKeyAction(path, KeyAction.NewMultiStringValue)));
        sub.Items.Add(Item(Strings.NewExpandableStringValue, "\uE8AB", () => props.OnKeyAction(path, KeyAction.NewExpandStringValue)));
        return sub;
    }

    private static MenuFlyoutItem Item(string text, string glyph, Action action, bool isEnabled = true)
    {
        var item = new MenuFlyoutItem
        {
            Text = text,
            IsEnabled = isEnabled,
            Icon = Icon(glyph),
        };
        item.Click += (_, _) => action();
        return item;
    }

    private static FontIcon Icon(string glyph) =>
        new() { Glyph = glyph, FontFamily = new FontFamily(Palette.IconFont) };

    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source is not null && source is not T)
            source = VisualTreeHelper.GetParent(source);
        return source as T;
    }

    /// <summary>
    /// Rebuilds the full path from the node hierarchy. More reliable than a
    /// name → path map, because sibling keys can share a display name.
    /// </summary>
    private static string? PathFromNode(TreeViewNode? node)
    {
        var parts = new List<string>();
        for (var current = node; current is not null; current = current.Parent)
        {
            if (current.Content is not TreeViewNodeData data) return null;
            if (data.Content == "Loading...") return null;
            parts.Add(data.Content);
        }

        parts.Reverse();
        if (parts.Count > 0 && parts[0] == "Computer") parts.RemoveAt(0);
        return parts.Count == 0 ? null : string.Join('\\', parts);
    }

    // ── Tree data ────────────────────────────────────────────────────

    private static TreeViewNodeData[] BuildNodes(
        Dictionary<string, string> pathMap,
        HashSet<string> expandedPaths,
        Dictionary<string, RegistryKeyEntry[]> treeChildren)
    {
        var rootKeys = RegistryService.GetRootKeys();
        var roots = new List<TreeViewNodeData>();

        foreach (var rootKey in rootKeys)
        {
            pathMap[rootKey.Name] = rootKey.FullPath;

            var children = BuildChildNodes(rootKey.FullPath, pathMap, expandedPaths, treeChildren);
            roots.Add(new TreeViewNodeData(rootKey.Name, children)
            {
                IsExpanded = expandedPaths.Contains(rootKey.FullPath)
            });
        }

        // Add "Computer" root that contains all hives
        return [new TreeViewNodeData("Computer", roots.ToArray()) { IsExpanded = true }];
    }

    private static TreeViewNodeData[]? BuildChildNodes(
        string parentPath,
        Dictionary<string, string> pathMap,
        HashSet<string> expandedPaths,
        Dictionary<string, RegistryKeyEntry[]> treeChildren)
    {
        if (!treeChildren.TryGetValue(parentPath, out var children) || children.Length == 0)
        {
            return [new TreeViewNodeData("Loading...")];
        }

        var nodes = new List<TreeViewNodeData>();
        foreach (var child in children.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
        {
            pathMap[child.Name] = child.FullPath;

            TreeViewNodeData[]? grandChildren = null;
            if (expandedPaths.Contains(child.FullPath))
            {
                grandChildren = BuildChildNodes(child.FullPath, pathMap, expandedPaths, treeChildren);
            }
            else if (child.HasChildren)
            {
                grandChildren = [new TreeViewNodeData("Loading...")];
            }

            nodes.Add(new TreeViewNodeData(child.Name, grandChildren)
            {
                IsExpanded = expandedPaths.Contains(child.FullPath)
            });
        }

        return nodes.Count > 0 ? nodes.ToArray() : null;
    }
}
