namespace Lupe.Platform.MacOS.Input;

/// <summary>
/// Windows-Virtual-Key-Codes (das gemeinsame Vokabular von Lupe) ↔ macOS-Tastencodes (<c>kVK_*</c> aus
/// HIToolbox/Events.h). Die macOS-Codes bezeichnen die Position der Taste (US-ANSI-Belegung); Buchstaben und Ziffern
/// ordnet <see cref="MacOSKeyboardLayout"/> – wenn möglich – über das aktuelle Tastaturlayout zu.
/// </summary>
internal static class MacKeyCodes
{
    // Umschalttasten
    public const ushort Command = 0x37, Shift = 0x38, CapsLock = 0x39, Option = 0x3A, Control = 0x3B;
    public const ushort RightCommand = 0x36, RightShift = 0x3C, RightOption = 0x3D, RightControl = 0x3E;
    public const ushort Return = 0x24, Tab = 0x30, Space = 0x31;

    /// <summary>(Virtual-Key, macOS-Tastencode) – eindeutig in beide Richtungen.</summary>
    private static readonly (int Vk, ushort Mac)[] Table =
    [
        // Buchstaben (Position wie US-ANSI)
        ('A', 0x00), ('S', 0x01), ('D', 0x02), ('F', 0x03), ('H', 0x04), ('G', 0x05), ('Z', 0x06), ('X', 0x07),
        ('C', 0x08), ('V', 0x09), ('B', 0x0B), ('Q', 0x0C), ('W', 0x0D), ('E', 0x0E), ('R', 0x0F), ('Y', 0x10),
        ('T', 0x11), ('O', 0x1F), ('U', 0x20), ('I', 0x22), ('P', 0x23), ('L', 0x25), ('J', 0x26), ('K', 0x28),
        ('N', 0x2D), ('M', 0x2E),
        // Ziffern
        ('1', 0x12), ('2', 0x13), ('3', 0x14), ('4', 0x15), ('6', 0x16), ('5', 0x17), ('9', 0x19), ('7', 0x1A),
        ('8', 0x1C), ('0', 0x1D),
        // OEM-Tasten nach US-Position (VK_OEM_*)
        (0xBB, 0x18), // = (VK_OEM_PLUS)
        (0xBD, 0x1B), // - (VK_OEM_MINUS)
        (0xDD, 0x1E), // ] (VK_OEM_6)
        (0xDB, 0x21), // [ (VK_OEM_4)
        (0xDE, 0x27), // ' (VK_OEM_7)
        (0xBA, 0x29), // ; (VK_OEM_1)
        (0xDC, 0x2A), // \ (VK_OEM_5)
        (0xBC, 0x2B), // , (VK_OEM_COMMA)
        (0xBF, 0x2C), // / (VK_OEM_2)
        (0xBE, 0x2F), // . (VK_OEM_PERIOD)
        (0xC0, 0x32), // ` (VK_OEM_3); auf ISO-Tastaturen die Taste neben der linken Umschalttaste
        (0xE2, 0x0A), // VK_OEM_102 ↔ kVK_ISO_Section (auf ISO-Tastaturen links neben der 1)
        // Steuertasten
        (0x0D, Return), (0x09, Tab), (0x20, Space), (0x08, 0x33), (0x1B, 0x35), (0x2E, 0x75), (0x2D, 0x72),
        (0x24, 0x73), (0x23, 0x77), (0x21, 0x74), (0x22, 0x79), (0x25, 0x7B), (0x27, 0x7C), (0x28, 0x7D), (0x26, 0x7E),
        (0x14, CapsLock), (0x0C, 0x47),
        // Umschalttasten links/rechts
        (0xA0, Shift), (0xA1, RightShift), (0xA2, Control), (0xA3, RightControl), (0xA4, Option), (0xA5, RightOption),
        (0x5B, Command), (0x5C, RightCommand),
        // F1–F20
        (0x70, 0x7A), (0x71, 0x78), (0x72, 0x63), (0x73, 0x76), (0x74, 0x60), (0x75, 0x61), (0x76, 0x62), (0x77, 0x64),
        (0x78, 0x65), (0x79, 0x6D), (0x7A, 0x67), (0x7B, 0x6F), (0x7C, 0x69), (0x7D, 0x6B), (0x7E, 0x71), (0x7F, 0x6A),
        (0x80, 0x40), (0x81, 0x4F), (0x82, 0x50), (0x83, 0x5A),
        // Ziffernblock
        (0x60, 0x52), (0x61, 0x53), (0x62, 0x54), (0x63, 0x55), (0x64, 0x56), (0x65, 0x57), (0x66, 0x58), (0x67, 0x59),
        (0x68, 0x5B), (0x69, 0x5C), (0x6A, 0x43), (0x6B, 0x45), (0x6D, 0x4E), (0x6E, 0x41), (0x6F, 0x4B),
    ];

    private static readonly Dictionary<int, ushort> VkToMacMap = Table.ToDictionary(t => t.Vk, t => t.Mac);
    private static readonly Dictionary<ushort, int> MacToVkMap = Table.ToDictionary(t => t.Mac, t => t.Vk);

    /// <summary>Alle Paare (für Tests).</summary>
    internal static IReadOnlyList<(int Vk, ushort Mac)> All => Table;

    /// <summary>Der macOS-Tastencode zur Taste (nach Position); null, wenn es keine passende gibt.</summary>
    public static ushort? FromVirtualKey(int vk) => VkToMacMap.TryGetValue(vk, out var mac) ? mac : null;

    /// <summary>Der Virtual-Key-Code zum macOS-Tastencode; null, wenn unbekannt.</summary>
    public static int? ToVirtualKey(ushort mac) => MacToVkMap.TryGetValue(mac, out var vk) ? vk : null;

    /// <summary>
    /// Die macOS-Tastencodes, die <see cref="Lupe.Core.Platform.IKeyboardOutput.IsDown"/> für einen Virtual-Key prüfen muss
    /// (die allgemeinen Umschalt-, Strg- und Alt-Codes stehen für links und rechts).
    /// </summary>
    public static ReadOnlySpan<ushort> ForKeyState(int vk) => vk switch
    {
        0x10 => [Shift, RightShift],
        0x11 => [Control, RightControl],
        0x12 => [Option, RightOption],
        _ when FromVirtualKey(vk) is { } mac => vk switch
        {
            0xA0 => [Shift], 0xA1 => [RightShift], 0xA2 => [Control], 0xA3 => [RightControl],
            0xA4 => [Option], 0xA5 => [RightOption], 0x5B => [Command], 0x5C => [RightCommand],
            _ => new[] { mac },
        },
        _ => [],
    };

    /// <summary>Carbon-Modifikatoren (<c>cmdKey</c> …) für ein Lupe-Tastenkürzel: Win = Befehlstaste (⌘), Alt = Wahltaste (⌥).</summary>
    public static uint CarbonModifiers(Lupe.Core.Model.Hotkey hotkey)
    {
        uint m = 0;
        if (hotkey.Win) m |= Native.Carbon.cmdKey;
        if (hotkey.Shift) m |= Native.Carbon.shiftKey;
        if (hotkey.Alt) m |= Native.Carbon.optionKey;
        if (hotkey.Ctrl) m |= Native.Carbon.controlKey;
        return m;
    }
}
