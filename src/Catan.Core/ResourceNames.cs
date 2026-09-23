namespace Catan.Core;

/// <summary>
/// What players call the resources on screen: brick, wood, sheep, wheat, ore (the user's choice). The code keeps the
/// Resource enum names (Brick, Lumber, Wool, Grain, Ore), which saved games and tests depend on; only text uses these.
/// </summary>
public static class ResourceNames
{
    private static readonly string[] Names = { "brick", "wood", "sheep", "wheat", "ore" };

    /// <summary>"brick", "wood", ...; "a card" for an unknown resource.</summary>
    public static string Of(int resource) => resource is >= 0 and < GameConstants.ResourceCount ? Names[resource] : "a card";

    public static string Of(Resource resource) => Of((int)resource);
}
