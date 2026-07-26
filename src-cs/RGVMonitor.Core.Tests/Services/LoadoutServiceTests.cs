using RGVMonitor.Core.Services;
using Xunit;

namespace RGVMonitor.Core.Tests.Services;

public class LoadoutServiceTests
{
    // Estructura coregame: Loadouts[].{Subject, Loadout.Items[weaponUuid].Sockets[skinSocket].Item.ID}
    private const string CoreLoadouts = """
    {
      "Loadouts": [
        {
          "Subject": "p1",
          "Loadout": {
            "Items": {
              "weapon-vandal": {
                "Sockets": {
                  "bcef87d6-209b-46c6-8b19-fbe40bd95abc": { "Item": { "ID": "skin-prime-vandal" } }
                }
              }
            }
          }
        }
      ]
    }
    """;

    [Fact]
    public void ParseLoadouts_Coregame_MapsSubjectWeaponToSkinId()
    {
        var map = LoadoutService.ParseLoadouts(CoreLoadouts, isPregame: false);

        Assert.True(map.ContainsKey("p1"));
        Assert.Equal("skin-prime-vandal", map["p1"]["weapon-vandal"]);
    }

    [Fact]
    public async Task GetLoadouts_Coregame_FetchesAndParses()
    {
        var api = new FakeValorantApiClient()
            .When("/core-game/v1/matches/m1/loadouts", CoreLoadouts);
        var svc = new LoadoutService(api);

        var map = await svc.GetLoadoutsAsync("m1", isPregame: false);

        Assert.Equal("skin-prime-vandal", map["p1"]["weapon-vandal"]);
    }
}
