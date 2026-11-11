using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using MenuFlyoutItemBase = Microsoft.UI.Reactor.Core.MenuFlyoutItemBase;
using Microsoft.Win32;
using ReactorRegedit.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace ReactorRegedit.Components;

internal sealed record ValueListProps(
    string KeyPath,
    RegistryValueEntry[] Values,
    int SelectedIndex,
    Action<int> OnSelectionChanged,
    Action<RegistryValueEntry> OnModify,
    Action<RegistryValueEntry> OnModifyBinary,
    Action<RegistryValueEntry> OnDelete,
    Action<RegistryValueEntry> OnRename,
    Action<RegistryValueEntry> OnCopyName,
    Action<RegistryValueEntry> OnCopyData,
    Action OnNewKey,
    Action<RegistryValueKind> OnNewValue,
    Action OnRefresh,
    Action OnExport,
    Action OnCopyKeyPath,
    Action OnFind
);

internal sealed class ValueList : Component<ValueListProps>
{
    public override Element Render()
    {
        var header = BuildKeyHeader();
        var columns = BuildColumnHeader();

        var list = LazyVStack<RegistryValueEntry>(
            Props.Values,
            v => v.Name + v.Kind,
            (value, index) => RenderValueRow(value, index)
        ) with { EstimatedItemSize = 30, Spacing = 0 };

        var pane = Grid(
            [GridSize.Star()],
            [GridSize.Auto, GridSize.Auto, GridSize.Star()],
            header.Grid(row: 0, column: 0),
            columns.Grid(row: 1, column: 0),
            list.Grid(row: 2, column: 0)
        ).Background(Palette.BackgroundAlt);

        return pane.WithContextFlyout(MenuItems(BuildPaneMenu()));
    }

    // ── Key summary card ─────────────────────────────────────────────

    private Element BuildKeyHeader()
    {
        var displayPath = string.IsNullOrEmpty(Props.KeyPath) ? Strings.Computer : Props.KeyPath;

        return Border(
            VStack(6,
                HStack(8,
                    Dot(Palette.Blue),
                    TextBlock(Strings.CurrentKey)
                        .FontSize(9)
                        .SemiBold()
                        .Foreground(Palette.TextSecondary)
                        .Set(tb => tb.CharacterSpacing = 120)
                ),
                TextBlock(displayPath)
                    .FontSize(16)
                    .SemiBold()
                    .Foreground(Palette.TextPrimary)
                    .TextTrimming(TextTrimming.CharacterEllipsis)
                    .TextWrapping(TextWrapping.NoWrap),
                HStack(10,
                    Chip($"{Props.Values.Length} {Strings.ValuesSuffix}", Palette.Mint),
                    TextBlock(Strings.ContextHint)
                        .FontSize(10.5)
                        .Foreground(Palette.TextMuted)
                        .TextTrimming(TextTrimming.CharacterEllipsis)
                        .VAlign(VerticalAlignment.Center)
                )
            )
        )
        .Background(Palette.Surface)

        .WithBorder(Palette.BorderSoft)
        .Padding(16, 10, 16, 10);
    }

    private static Element BuildColumnHeader()
    {
        return Grid(
            [GridSize.Star(2), GridSize.Star(), GridSize.Star(3)],
            [GridSize.Px(30)],
            ColumnLabel(Strings.ColumnName, Palette.Blue).Grid(row: 0, column: 0),
            ColumnLabel(Strings.ColumnType, Palette.Purple).Grid(row: 0, column: 1),
            ColumnLabel(Strings.ColumnData, Palette.Mint).Grid(row: 0, column: 2)
        )
        .Padding(16, 0, 16, 0)
        .Background(Palette.SurfaceAlt)

        .WithBorder(Palette.BorderSoft);
    }

    private static Element ColumnLabel(string text, string color) =>
        HStack(6,
            Dot(color),
            TextBlock(text)
                .FontSize(9)
                .SemiBold()
                .Foreground(color)
                .Set(tb => tb.CharacterSpacing = 120)
                .VAlign(VerticalAlignment.Center)
        ).VAlign(VerticalAlignment.Center);

    // ── Value rows ───────────────────────────────────────────────────

    private Element RenderValueRow(RegistryValueEntry value, int index)
    {
        var accent = Palette.ForKind(value.Kind);
        var selected = index == Props.SelectedIndex;
        var baseBackground = index % 2 == 0 ? Palette.Row : Palette.RowAlt;
        var background = selected ? Palette.RowSelected : baseBackground;

        var nameColor = string.IsNullOrEmpty(value.Name) ? Palette.Amber : Palette.TextPrimary;

        var row = Grid(
            [GridSize.Px(3), GridSize.Star(2), GridSize.Star(), GridSize.Star(3)],
            [GridSize.Px(30)],
            Border(Empty())
                .Background(selected ? accent : "transparent")
                .Grid(row: 0, column: 0),
            HStack(8,
                TextBlock(Palette.IconForKind(value.Kind))
                    .FontFamily(Palette.IconFont)
                    .FontSize(13)
                    .Foreground(accent)
                    .VAlign(VerticalAlignment.Center),
                TextBlock(value.DisplayName)
                    .FontSize(12)
                    .Foreground(nameColor)
                    .TextTrimming(TextTrimming.CharacterEllipsis)
                    .TextWrapping(TextWrapping.NoWrap)
                    .VAlign(VerticalAlignment.Center)
            ).Padding(13, 0, 8, 0).Grid(row: 0, column: 1),
            TypeBadge(value, accent).Grid(row: 0, column: 2),
            TextBlock(value.DisplayData)
                .FontFamily(Palette.MonoFont)
                .FontSize(11)
                .Foreground(accent)
                .TextTrimming(TextTrimming.CharacterEllipsis)
                .TextWrapping(TextWrapping.NoWrap)
                .VAlign(VerticalAlignment.Center)
                .Grid(row: 0, column: 3)
        )
        .Padding(0, 0, 16, 0)
        .Background(background)

        .WithBorder(Palette.BorderSoft)
        .OnPointerEntered((s, _) =>
            ((Grid)s).Background = BrushHelper.Parse(Palette.RowHover))
        .OnPointerExited((s, _) =>
            ((Grid)s).Background = BrushHelper.Parse(selected ? Palette.RowSelected : baseBackground))
        .OnTapped((_, _) => Props.OnSelectionChanged(index))
        .OnDoubleTapped((_, _) => Props.OnModify(value));

        // Value-specific context menu. Right-clicking a row wins over the
        // pane-level menu wrapped around the whole list.
        return row.WithContextFlyout(MenuItems(
            MenuItem(Strings.Modify, () => Props.OnModify(value), "\uE70F"),
            MenuItem(Strings.ModifyBinaryData, () => Props.OnModifyBinary(value), "\uE9F5"),
            MenuSeparator(),
            MenuItem(Strings.CopyName, () => Props.OnCopyName(value), "\uE8C8"),
            MenuItem(Strings.CopyData, () => Props.OnCopyData(value), "\uE8C8"),
            MenuSeparator(),
            MenuItem(Strings.Rename, () => Props.OnRename(value), "\uE8AC"),
            MenuItem(Strings.Delete, () => Props.OnDelete(value), "\uE74D")
        ));
    }

    private static Element TypeBadge(RegistryValueEntry value, string color) =>
        Border(
            TextBlock(value.TypeName)
                .FontSize(9)
                .SemiBold()
                .Foreground(color)
        )
        .Background(Palette.Tint(color, 0x22))
        .BorderBrush(Palette.Tint(color, 0x55))
        .BorderThickness(1)
        .CornerRadius(4)
        .Padding(6, 1, 6, 1)
        .HAlign(HorizontalAlignment.Left)
        .VAlign(VerticalAlignment.Center);

    // ── Pane-level context menu ──────────────────────────────────────

    private MenuFlyoutItemBase[] BuildPaneMenu() =>
    [
        MenuSubItem(Strings.NewMenu, NewItems()),
        MenuSeparator(),
        MenuItem(Strings.Refresh, Props.OnRefresh, "\uE72C"),
        MenuItem(Strings.Find, Props.OnFind, "\uE721"),
        MenuSeparator(),
        MenuItem(Strings.Export, Props.OnExport, "\uE74E"),
        MenuItem(Strings.CopyKeyName, Props.OnCopyKeyPath, "\uE8C8"),
    ];

    private MenuFlyoutItemBase[] NewItems() =>
    [
        MenuItem(Strings.NewKey, Props.OnNewKey, "\uE8B7"),
        MenuSeparator(),
        MenuItem(Strings.NewStringValue, () => Props.OnNewValue(RegistryValueKind.String), "\uE8A5"),
        MenuItem(Strings.NewBinaryValue, () => Props.OnNewValue(RegistryValueKind.Binary), "\uE9F5"),
        MenuItem(Strings.NewDwordValue, () => Props.OnNewValue(RegistryValueKind.DWord), "\uE8EF"),
        MenuItem(Strings.NewQwordValue, () => Props.OnNewValue(RegistryValueKind.QWord), "\uE8EF"),
        MenuItem(Strings.NewMultiStringValue, () => Props.OnNewValue(RegistryValueKind.MultiString), "\uE8FD"),
        MenuItem(Strings.NewExpandableStringValue, () => Props.OnNewValue(RegistryValueKind.ExpandString), "\uE8AB"),
    ];

    // ── Small visual helpers ─────────────────────────────────────────

    private static Element Dot(string color) =>
        Border(Empty())
            .Width(6)
            .Height(6)
            .CornerRadius(3)
            .Background(color)
            .VAlign(VerticalAlignment.Center);

    private static Element Chip(string text, string color) =>
        Border(
            TextBlock(text)
                .FontSize(10)
                .SemiBold()
                .Foreground(color)
        )
        .Background(Palette.Tint(color, 0x20))
        .BorderBrush(Palette.Tint(color, 0x55))
        .BorderThickness(1)
        .CornerRadius(9)
        .Padding(8, 1, 8, 1)
        .HAlign(HorizontalAlignment.Left);
}
