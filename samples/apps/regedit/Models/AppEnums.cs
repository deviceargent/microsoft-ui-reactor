namespace ReactorRegedit.Models;

public enum ActiveDialog
{
    None,
    EditString,
    EditMultiString,
    EditDword,
    EditQword,
    EditBinary,
    Find,
    AddFavorite,
    RemoveFavorite,
    Export,
    RenameKey,
    RenameValue,
    About,
    ConnectRemote,
    LoadHive,
    UnloadHive,
}

[Flags]
public enum FindFlags
{
    None = 0,
    Keys = 1,
    Values = 2,
    Data = 4,
    WholeStringOnly = 8,
}

public enum ExportFormat
{
    RegistrationFiles,
    Win9xNt4Files,
}

public enum NumberBase
{
    Hexadecimal,
    Decimal,
}

public enum NewValueKind
{
    Key,
    StringValue,
    BinaryValue,
    DwordValue,
    QwordValue,
    MultiStringValue,
    ExpandableStringValue,
}

/// <summary>
/// Actions offered by the key/node context menu. The tree surfaces these and
/// the root component routes them to the existing command handlers.
/// </summary>
public enum KeyAction
{
    Open,
    NewKey,
    NewStringValue,
    NewBinaryValue,
    NewDwordValue,
    NewQwordValue,
    NewMultiStringValue,
    NewExpandStringValue,
    Rename,
    Delete,
    CopyKeyName,
    Export,
    Refresh,
    Find,
    AddFavorite,
}

/// <summary>Pending destructive action awaiting confirmation.</summary>
public sealed record DeleteRequest(string KeyPath, string ValueName, string Label, bool IsKey);
