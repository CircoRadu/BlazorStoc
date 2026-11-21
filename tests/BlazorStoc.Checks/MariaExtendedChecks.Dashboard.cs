using Microsoft.Extensions.Configuration;
using MySqlConnector;
using BlazorStoc.Services;

namespace BlazorStoc.Checks;

public static partial class MariaExtendedChecks
{
    // The arrangement of the personalised main page on MariaDB: per user, with zones (required title, own columns), tiles with their zone, labels, spaces, the buffer and the sizes of the sliders.
    private static async Task DashboardAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, MySqlConnection probe)
    {
        var suffix = Suffix();
        var users = new MariaUserRepository(configuration, admin, audit);
        var userA = await users.CreateAsync(new WebUserInput { Username = "ext.da." + suffix, DisplayName = "Ext Dashboard A", Role = AccessRoles.LimitedUser, Password = "Parola-ext-veche-123!" });
        var userB = await users.CreateAsync(new WebUserInput { Username = "ext.db." + suffix, DisplayName = "Ext Dashboard B", Role = AccessRoles.LimitedUser, Password = "Parola-ext-veche-123!" });
        var repositoryA = new MariaUserDashboardRepository(configuration, new TaskTestAccess(userA.Id, userA.Username, false));
        var repositoryB = new MariaUserDashboardRepository(configuration, new TaskTestAccess(userB.Id, userB.Username, false));
        var keys = MenuCatalog.Entries.Take(5).Select(entry => entry.Key).ToArray();
        try
        {
            Check(!(await repositoryA.GetAsync()).Custom && (await repositoryA.GetAsync()).ZoneList.Count == 1, "Dashboard: a user who arranged nothing sees the standard page, in one zone");

            var layout = DashboardLayout.From(DashboardState.Empty);
            layout.Zones[0].Title = "Vanzari";
            layout.AddZone("Stoc");
            layout.SetColumns(1, 2); layout.SetPlacement(1, DashboardZone.Right);
            layout.AddRow(0, DashboardLayout.TilesKind, 0); layout.AddRow(0, DashboardRow.Spacer, 1); layout.AddRow(0, DashboardRow.Label, 2); layout.Zones[0].Rows[2].Text = "Eticheta"; layout.Zones[0].Rows[1].Color = "#00aa00"; layout.Zones[0].Rows[2].Color = "#AA0000";
            layout.AddRow(1, DashboardLayout.TilesKind, 0);
            foreach (var key in keys.Take(4)) layout.AddTile(key);
            layout.MoveTile(keys[0], 0, 0, 0); layout.MoveTile(keys[1], 0, 0, 3); layout.MoveTile(keys[2], 1, 0, 1);
            layout.Sizes = new DashboardSizes(320, 100, 30);
            await repositoryA.SaveAsync(layout.ToState());

            var read = await repositoryA.GetAsync();
            var back = DashboardLayout.From(read);
            Check(read.Custom && read.ZoneList.Count == 2 && read.ZoneList[0].Title == "Vanzari" && read.ZoneList[1].Title == "Stoc" && read.ZoneList[1].Columns == 2 && read.ZoneList[0].Placement == DashboardZone.Beside && read.ZoneList[1].Placement == DashboardZone.Right, "Dashboard: the zones are saved with their title, order and columns");
            Check(back.KeyAt(0, 0, 0) == keys[0] && back.KeyAt(0, 0, 3) == keys[1] && back.KeyAt(1, 0, 1) == keys[2] && back.Buffer.SequenceEqual([keys[3]]), "Dashboard: the tiles come back in their zone, row and column, the waiting one in the buffer");
            Check(back.Zones[0].Rows[1].Kind == DashboardRow.Spacer && back.Zones[0].Rows[1].Color == "#00aa00" && back.Zones[0].Rows[2].Text == "Eticheta" && back.Zones[0].Rows[2].Color == "#aa0000", "Dashboard: labels and spaces are saved per zone (the colour in lower case)");
            Check(read.SizeSettings == new DashboardSizes(320, 100, 30), "Dashboard: the sizes of the sliders are saved with the display settings of the user");

            // Another user keeps their own page.
            Check(!(await repositoryB.GetAsync()).Custom, "Dashboard: the arrangement belongs to its user alone");

            // A zone without a title, too many zones, two tiles on a cell and tiles in a row of a label are refused.
            await Rejects<InvalidOperationException>(() => repositoryA.SaveAsync(read with { Zones = [new DashboardZone(0, "  ", 4)] }), "Dashboard: a zone without a title is not saved");
            await Rejects<InvalidOperationException>(() => repositoryA.SaveAsync(read with { Zones = [.. Enumerable.Range(0, DashboardZone.MaxZones + 1).Select(index => new DashboardZone(index, "Z" + index, 4))] }), "Dashboard: more zones than the limit are not saved");
            await Rejects<InvalidOperationException>(() => repositoryA.SaveAsync(new DashboardState(4, [new DashboardTile(keys[0], 0, 0, 1), new DashboardTile(keys[1], 0, 0, 1)], [], true, read.Zones)), "Dashboard: two tiles on one cell of a zone are not saved");
            await Rejects<InvalidOperationException>(() => repositoryA.SaveAsync(new DashboardState(4, [new DashboardTile(keys[0], 0, 0, 0)], [new DashboardRow(0, DashboardRow.Label, "x", "", 0)], true, read.Zones)), "Dashboard: a row with a label cannot hold tiles");
            Check((await repositoryA.GetAsync()).ZoneList.Count == 2, "Dashboard: a refused save leaves the saved page as it was");

            // The same cell of two zones is allowed; a tile in the second zone does not touch the first.
            var twin = new DashboardState(4, [new DashboardTile(keys[0], 0, 0, 0), new DashboardTile(keys[1], 0, 0, 1)], [], true, read.Zones, new DashboardSizes(0, 0, 0));
            await repositoryA.SaveAsync(twin);
            var twinRead = await repositoryA.GetAsync();
            Check(twinRead.Tiles.Count == 2 && twinRead.Tiles.Select(tile => tile.Zone).Distinct().Count() == 2 && twinRead.SizeSettings == DashboardSizes.Standard && twinRead.Tiles.All(tile => tile.Row == 0 && tile.Col == 0), "Dashboard: the same cell of two zones holds two tiles, each where it was put (reading does not re-place them); standard sizes are saved as standard");

            // A page saved without zone rows (the arrangement from before the zones) opens in one zone named Dashboard.
            await using (var legacy = new MySqlCommand("DELETE FROM user_dashboard_zones WHERE user_id=@user", probe)) { legacy.Parameters.AddWithValue("@user", userA.Id); await legacy.ExecuteNonQueryAsync(); }
            var legacyRead = await repositoryA.GetAsync();
            Check(legacyRead.Custom && legacyRead.ZoneList.Count == 1 && legacyRead.ZoneList[0].Title == DashboardZone.DefaultTitle, "Dashboard: an arrangement without zone rows opens in one zone named Dashboard");

            // An empty page is the user's choice and keeps its zones; the standard page keeps nothing.
            await repositoryA.SaveAsync(new DashboardState(4, [], [], true, [new DashboardZone(0, "Gol", 4)]));
            var empty = await repositoryA.GetAsync();
            Check(empty.Custom && empty.Tiles.Count == 0 && empty.ZoneList.Count == 1 && empty.ZoneList[0].Title == "Gol", "Dashboard: an empty page stays empty and keeps its zone");
            await repositoryA.SaveAsync(new DashboardState(4, [], [], false));
            var standard = await repositoryA.GetAsync();
            Check(!standard.Custom && standard.Tiles.Count == 0 && standard.ZoneList[0].Title == DashboardZone.DefaultTitle, "Dashboard: back to the standard page keeps no zone");
        }
        finally
        {
            foreach (var user in new[] { userA, userB })
                if ((await users.GetUsersAsync()).FirstOrDefault(item => item.Id == user.Id) is { } live) await users.DeleteAsync(live, "Ext curatare");
        }
    }
}
