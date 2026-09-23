using Catan.Core;
using Catan.UI;
using Godot;

/// <summary>A row of five −/+ counters, one per resource, bound to a <see cref="CardPicker"/>.</summary>
public partial class CardPickerView : HBoxContainer
{
    private static readonly string[] Short = { "Brick", "Lumber", "Wool", "Grain", "Ore" };
    private readonly Label[] _counts = new Label[GameConstants.ResourceCount];
    private readonly Button[] _minus = new Button[GameConstants.ResourceCount];
    private readonly Button[] _plus = new Button[GameConstants.ResourceCount];

    /// <summary>Godot needs a parameterless constructor (editor hot-reload); code always passes a picker.</summary>
    public CardPickerView() : this(new CardPicker())
    {
    }

    public CardPickerView(CardPicker picker)
    {
        Picker = picker;
        AddThemeConstantOverride("separation", 4);
        for (int r = 0; r < GameConstants.ResourceCount; r++)
        {
            int resource = r;
            var column = new VBoxContainer { CustomMinimumSize = new Vector2(62, 0) };
            column.AddThemeConstantOverride("separation", 1);
            var name = Ui.Label(Short[r], 12, Ui.MutedText);
            name.HorizontalAlignment = HorizontalAlignment.Center;
            column.AddChild(name);

            var row = new HBoxContainer { Alignment = AlignmentMode.Center };
            row.AddThemeConstantOverride("separation", 2);
            _minus[r] = SmallButton("−", () => Picker.Remove(resource));
            _counts[r] = Ui.Label("0", 15);
            _counts[r].CustomMinimumSize = new Vector2(16, 0);
            _counts[r].HorizontalAlignment = HorizontalAlignment.Center;
            _plus[r] = SmallButton("+", () => Picker.Add(resource));
            row.AddChild(_minus[r]);
            row.AddChild(_counts[r]);
            row.AddChild(_plus[r]);
            column.AddChild(row);
            AddChild(column);
        }
        Sync();
    }

    public CardPicker Picker { get; }

    // Subscribe while on screen: the view may be removed and re-added (e.g. the discard picker), and _Ready runs only once.
    public override void _EnterTree()
    {
        Picker.Changed += Sync;
        Sync();
    }

    public override void _ExitTree() => Picker.Changed -= Sync;

    private void Sync()
    {
        for (int r = 0; r < GameConstants.ResourceCount; r++)
        {
            _counts[r].Text = Picker[r].ToString();
            _minus[r].Disabled = !Picker.CanRemove(r);
            _plus[r].Disabled = !Picker.CanAdd(r);
        }
    }

    private static Button SmallButton(string text, System.Action onClick)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(20, 22) };
        button.AddThemeFontSizeOverride("font_size", 13);
        button.Pressed += onClick;
        return button;
    }
}
