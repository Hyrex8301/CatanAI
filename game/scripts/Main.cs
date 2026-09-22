using Godot;

public partial class Main : Node2D
{
    public override void _Ready() =>
        GetNode<Label>("Label").Text = $"Catan.Core says: {Catan.Core.Info.HexCount} hexes";
}
