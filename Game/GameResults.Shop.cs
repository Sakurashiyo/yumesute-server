static partial class GameResults
{
    public static object?[] ShopPurchaseNotifications()
    {
        return new object?[]
        {
            new object?[] { 0, new object?[] { 100400, 100401 } },
            new object?[] { 0, new object?[] { 100300, 100301 } }
        };
    }

    public static object?[] ExchangeShopThingResult(long exchangeShopThingId, int quantity)
    {
        var thingId = exchangeShopThingId switch
        {
            101004 => 130040L,
            35013037 => 163004L,
            35013179 => 130043L,
            _ => exchangeShopThingId
        };

        return new object?[]
        {
            new object?[] { 1, thingId, Math.Max(quantity, 1), null, null, null, false }
        };
    }

    public static object?[] ExchangeShopThingPresentData(long exchangeShopThingId, int quantity)
    {
        var reward = ExchangeShopThingResult(exchangeShopThingId, quantity).OfType<object?[]>().FirstOrDefault();
        var itemMasterId = reward?.ElementAtOrDefault(1) as long? ?? exchangeShopThingId;
        var itemQuantity = reward?.ElementAtOrDefault(2) as int? ?? Math.Max(quantity, 1);

        return new object?[]
        {
            new object?[] { 102, new object?[] { Random.Shared.Next(44000000, 44999999), exchangeShopThingId, 0, null, quantity, null } },
            new object?[] { 27, new object?[] { Random.Shared.Next(151000000, 151999999), itemMasterId, itemQuantity } },
            new object?[] { 27, new object?[] { Random.Shared.Next(151000000, 151999999), 4235013, Math.Max(0, 3000 - quantity) } }
        };
    }

    public static object?[] MarketResult()
    {
        return new object?[]
        {
            new object?[]
            {
                new object?[] { 1, 105, null, null, false, 10 },
                new object?[] { 2, 201, null, null, false, null },
                new object?[] { 3, 301, null, null, false, null },
                new object?[] { 4, 404, null, null, false, null },
                new object?[] { 5, 506, null, null, false, null },
                new object?[] { 6, 604, null, null, false, null },
                new object?[] { 7, 701, null, null, false, null },
                new object?[] { 8, 809, null, null, false, null },
                new object?[] { 9, 902, null, null, false, null },
                new object?[] { 10, 1001, null, null, false, 20 },
                new object?[] { 11, 1101, null, null, false, null },
                new object?[] { 12, 1201, null, null, false, null },
                new object?[] { 13, 1319, null, null, false, null },
                new object?[] { 14, 1407, null, null, false, null },
                new object?[] { 15, 1520, null, null, false, null }
            },
            20
        };
    }

    public static object?[] RefreshMarketPresentData(bool withJewel)
    {
        if (!withJewel) return Array.Empty<object?>();

        return new object?[]
        {
            new object?[] { 64, new object?[] { Random.Shared.Next(300000, 399999), DateTime.UtcNow, 1 } },
            new object?[] { 128, new object?[] { Random.Shared.Next(5000000, 5999999), 3025, 450, 0 } }
        };
    }

    public static object?[] ExchangeMarketThingsResult()
    {
        return new object?[]
        {
            new object?[] { 1, 130061, 250, null, null, null, false }
        };
    }

    public static object?[] ExchangeMarketThingsPresentData()
    {
        return new object?[]
        {
            new object?[] { 128, new object?[] { Random.Shared.Next(5000000, 5999999), 3025, 460, 0 } },
            new object?[] { 27, new object?[] { Random.Shared.Next(151000000, 151999999), 130061, 280 } }
        };
    }

    public static object?[] ExchangeMusicResult(long musicMasterId)
    {
        return new object?[] { 14, musicMasterId, 1, null, null, null, false };
    }

    public static object?[] ExchangeMusicPresentData(long musicMasterId)
    {
        var exchangeItemCount = musicMasterId switch
        {
            146 => 40,
            145 => 30,
            68 => 20,
            _ => 31
        };

        var presentData = new List<object?>
        {
            DataObject(25, new object?[] { Random.Shared.Next(18000000, 18999999), null, musicMasterId, null, null, false, null, 0, 0, true }),
            DataObject(27, new object?[] { 151612557L, 130001, exchangeItemCount })
        };

        if (musicMasterId == 146)
        {
            presentData.Add(DataObject(48, new object?[] { Random.Shared.Next(126000000, 126999999), false, false, 1, null, 18, 18 }));
            presentData.Add(DataObject(48, new object?[] { Random.Shared.Next(126000000, 126999999), true, false, 1, null, 17, 17 }));
            presentData.Add(DataObject(48, new object?[] { Random.Shared.Next(126000000, 126999999), false, false, 6, null, 42, 42 }));
        }
        else if (musicMasterId is 145 or 68)
        {
            var progress = musicMasterId == 145 ? 2 : 3;
            presentData.Add(DataObject(48, new object?[] { Random.Shared.Next(126000000, 126999999), true, false, progress, null, 17, 17 }));
        }

        return presentData.ToArray();
    }

    public static object?[] ExchangeMusicNotifications(long musicMasterId)
    {
        if (musicMasterId != 146) return Array.Empty<object?>();

        return new object?[]
        {
            new object?[] { 0, new object?[] { 17, 17 } }
        };
    }

    public static object?[] PosterLevelUpPresentData(long posterId, int levelTo)
    {
        return new object?[]
        {
            new object?[] { 27, new object?[] { Random.Shared.Next(151000000, 151999999), 130061, Math.Max(0, 40 - (levelTo * 5)) } },
            new object?[] { 11, new object?[] { posterId, 230940, levelTo, 0, 0, 0, false, 0 } },
            new object?[] { 67, new object?[] { Random.Shared.Next(5000000, 5999999), 0.4, 0.1 } }
        };
    }
}
