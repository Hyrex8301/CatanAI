using Godot;

/// <summary>
/// Developer screenshots, for checking the screens without clicking through them. Set CATAN_SHOT to a .png path to save a
/// screenshot after CATAN_SHOT_AFTER seconds (default 3) and quit. CATAN_SHOT_COUNT=N (with "{n}" in the path) takes N
/// shots CATAN_SHOT_EVERY seconds apart, to catch animations. The game screen also reads CATAN_SEED (which game),
/// CATAN_AUTOPLAY=N (a bot plays your seat for the first N actions) and CATAN_ANIMATE=1 (keep animations while it does).
/// </summary>
public static class DevShots
{
    public static async void Run(Node node)
    {
        string? path = System.Environment.GetEnvironmentVariable("CATAN_SHOT");
        if (string.IsNullOrEmpty(path))
            return;
        double after = double.TryParse(System.Environment.GetEnvironmentVariable("CATAN_SHOT_AFTER"), out double s) ? s : 3;
        int count = int.TryParse(System.Environment.GetEnvironmentVariable("CATAN_SHOT_COUNT"), out int c) ? c : 1;
        double every = double.TryParse(System.Environment.GetEnvironmentVariable("CATAN_SHOT_EVERY"), out double e) ? e : 0.2;
        var tree = node.GetTree();
        await node.ToSignal(tree.CreateTimer(after), SceneTreeTimer.SignalName.Timeout);
        for (int n = 0; n < count; n++)
        {
            if (n > 0)
                await node.ToSignal(tree.CreateTimer(every), SceneTreeTimer.SignalName.Timeout);
            await node.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            node.GetViewport().GetTexture().GetImage().SavePng(path.Replace("{n}", n.ToString("00")));
        }
        tree.Quit();
    }
}
