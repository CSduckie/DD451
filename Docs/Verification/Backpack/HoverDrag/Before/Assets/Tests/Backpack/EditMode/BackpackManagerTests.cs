using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class BackpackManagerTests
{
    private readonly List<UnityEngine.Object> createdObjects = new List<UnityEngine.Object>();
    private ItemManager items;
    private BackpackManager backpack;
    private PlayerController player;
    private PlayerHealth health;
    private int nextDefinition;

    [SetUp]
    public void SetUp()
    {
        GameObject module = CreateObject("Backpack Manager Test");
        items = module.AddComponent<ItemManager>();
        backpack = module.AddComponent<BackpackManager>();
        backpack.config = CreateAsset<BackpackConfigSO>();
        Assert.That(backpack.Initialize(items), Is.True);
        Assert.That(items.BeginRound(1), Is.True);

        PlayerConfigSO playerConfig = CreateAsset<PlayerConfigSO>();
        playerConfig.maxHealth = 3;
        playerConfig.damageInvulnerabilitySeconds = 0f;
        player = CreateObject("Real Backpack Test Player").AddComponent<PlayerController>();
        player.config = playerConfig;
        health = player.GetComponent<PlayerHealth>();
        health.config = playerConfig;
        Assert.That(player.ResetForExploration(1, Vector3.zero), Is.True);
    }

    [TearDown]
    public void TearDown()
    {
        for (int i = createdObjects.Count - 1; i >= 0; i--)
        {
            if (createdObjects[i] != null) { UnityEngine.Object.DestroyImmediate(createdObjects[i]); }
        }
        createdObjects.Clear();
    }

    [Test]
    public void MoveAndUseRequireRealOwnershipContainerAndLegalMembership()
    {
        ItemInstance unplaced = CreateItem(false);
        Assert.That(backpack.TryMove(unplaced.InstanceId, 0, 0, false), Is.EqualTo(BackpackResult.MissingPlacement));
        Assert.That(backpack.TryUseItem(unplaced.InstanceId, health, out _), Is.EqualTo(BackpackResult.MissingPlacement));
        Assert.That(backpack.TryAddPlacement(unplaced.InstanceId, 0, 0, false), Is.EqualTo(BackpackResult.Success));
        Assert.That(items.TrySetOwner(unplaced.InstanceId, ItemOwner.Backpack, "different-backpack"), Is.True);
        Assert.That(backpack.TryMove(unplaced.InstanceId, 1, 0, false), Is.EqualTo(BackpackResult.WrongContainer));
        Assert.That(backpack.TryUseItem(unplaced.InstanceId, health, out _), Is.EqualTo(BackpackResult.WrongContainer));
        Assert.That(items.TrySetOwner(unplaced.InstanceId, ItemOwner.Storage, "storage"), Is.True);
        Assert.That(backpack.TryMove(unplaced.InstanceId, 1, 0, false), Is.EqualTo(BackpackResult.WrongOwner));
        Assert.That(backpack.TryUseItem(unplaced.InstanceId, health, out _), Is.EqualTo(BackpackResult.WrongOwner));
        Assert.That(backpack.GetPlacements()[0].X, Is.Zero);
        Assert.That(items.TrySetOwner(unplaced.InstanceId, ItemOwner.Backpack, backpack.ContainerId), Is.True);
        unplaced.Definition.width = 5;
        Assert.That(backpack.TryUseItem(unplaced.InstanceId, health, out _), Is.EqualTo(BackpackResult.OutOfBounds),
            "有摆放记录但当前尺寸已经非法，也不能绕过占格校验使用。");
        Assert.That(unplaced.UsesThisRound, Is.Zero);
        Assert.That(health.CurrentHealth, Is.EqualTo(3));
    }

    [Test]
    public void ExternalPreviewAndSilentLayoutEditsNeverChangeSourceOwnership()
    {
        ItemInstance external = CreateItem(false, ItemOwner.Container, "box-original");
        int notifications = 0;
        backpack.Changed += () => notifications++;
        Assert.That(backpack.CanPlaceExternal(external.InstanceId, 0, 0, false), Is.EqualTo(BackpackResult.Success));
        Assert.That(backpack.CanPlaceExternal(external.InstanceId, 4, 4, true), Is.EqualTo(BackpackResult.OutOfBounds));
        Assert.That(backpack.GetPlacements().Count, Is.Zero);
        Assert.That(external.ContainerId, Is.EqualTo("box-original"));

        Assert.That(backpack.TryAddPlacement(external.InstanceId, 0, 0, false), Is.EqualTo(BackpackResult.Success));
        Assert.That(backpack.TryAddPlacement(external.InstanceId, 2, 2, false), Is.EqualTo(BackpackResult.DuplicateInstance));
        Assert.That(backpack.TryRemovePlacement(external.InstanceId, out BackpackPlacement removed), Is.EqualTo(BackpackResult.Success));
        Assert.That(backpack.TryRestorePlacement(removed), Is.EqualTo(BackpackResult.Success));
        Assert.That(notifications, Is.Zero);
        Assert.That(external.Owner, Is.EqualTo(ItemOwner.Container));
        Assert.That(external.ContainerId, Is.EqualTo("box-original"));
        Assert.That(backpack.TryGetCarriedItem(external.InstanceId, out _), Is.EqualTo(BackpackResult.WrongOwner));
        // 这里只证明06的静默布局入口；没有完成真实拾取或跨来源转移。
    }

    [Test]
    public void ReinitializeAndRepeatedRefreshPreserveLayoutIdentityAndRoundState()
    {
        ItemInstance medkit = CreateItem(false);
        Assert.That(backpack.TryAddPlacement(medkit.InstanceId, 1, 2, true), Is.EqualTo(BackpackResult.Success));
        Assert.That(health.TakeDamage(1), Is.True);
        Assert.That(backpack.TryUseItem(medkit.InstanceId, health, out ItemUseResult used), Is.EqualTo(BackpackResult.Success));
        Assert.That(used, Is.EqualTo(ItemUseResult.Success));
        IReadOnlyList<BackpackPlacement> before = backpack.GetPlacements();
        backpack.config.width = 9;
        Assert.That(backpack.Initialize(items), Is.True);
        backpack.NotifyChanged();
        backpack.NotifyChanged();
        Assert.That(backpack.Width, Is.EqualTo(4));
        CollectionAssert.AreEqual(before, backpack.GetPlacements());
        Assert.That(medkit.UsesThisRound, Is.EqualTo(1));
        Assert.That(items.CurrentRound, Is.EqualTo(1));
        Assert.That(items.InstanceCount, Is.EqualTo(1));
    }

    [Test]
    public void FullHealthRefusesFoodAndMedkitWithoutFreeingCellsOrUsingAllowance()
    {
        ItemInstance medkit = CreateItem(false);
        ItemInstance food = CreateItem(true);
        backpack.TryAddPlacement(medkit.InstanceId, 0, 0, false);
        backpack.TryAddPlacement(food.InstanceId, 2, 0, false);
        IReadOnlyList<BackpackPlacement> before = backpack.GetPlacements();
        Assert.That(backpack.TryUseItem(medkit.InstanceId, health, out ItemUseResult medkitResult), Is.EqualTo(BackpackResult.Success));
        Assert.That(backpack.TryUseItem(food.InstanceId, health, out ItemUseResult foodResult), Is.EqualTo(BackpackResult.Success));
        Assert.That(medkitResult, Is.EqualTo(ItemUseResult.FullHealth));
        Assert.That(foodResult, Is.EqualTo(ItemUseResult.FullHealth));
        Assert.That(medkit.UsesThisRound, Is.Zero);
        Assert.That(food.UsesThisRound, Is.Zero);
        Assert.That(food.Owner, Is.EqualTo(ItemOwner.Backpack));
        CollectionAssert.AreEqual(before, backpack.GetPlacements());
    }

    [Test]
    public void FoodRemovesOnlyUsedInstanceBeforeNotificationAndBlocksReentry()
    {
        ItemInstance foodA = CreateItem(true);
        Assert.That(items.TryCreateInstance(foodA.Definition, ItemOwner.Backpack, backpack.ContainerId, "test", "",
            out ItemInstance foodB), Is.True);
        backpack.TryAddPlacement(foodA.InstanceId, 0, 0, false);
        backpack.TryAddPlacement(foodB.InstanceId, 2, 0, false);
        Assert.That(health.TakeDamage(2), Is.True);
        int notifications = 0;
        BackpackResult callbackMove = BackpackResult.Success;
        BackpackResult callbackUse = BackpackResult.Success;
        bool consumedLayoutRemoved = false;
        health.HealthChanged += (current, maximum) =>
        {
            callbackMove = backpack.TryRemovePlacement(foodA.InstanceId, out _);
        };
        backpack.Changed += () =>
        {
            notifications++;
            consumedLayoutRemoved = foodA.Owner == ItemOwner.Consumed
                && !backpack.TryGetPlacement(foodA.InstanceId, out _)
                && backpack.TryGetPlacement(foodB.InstanceId, out _);
            callbackUse = backpack.TryUseItem(foodB.InstanceId, health, out _);
            backpack.NotifyChanged();
        };

        Assert.That(backpack.TryUseItem(foodA.InstanceId, health, out ItemUseResult result), Is.EqualTo(BackpackResult.Success));
        Assert.That(result, Is.EqualTo(ItemUseResult.Success));
        Assert.That(callbackMove, Is.EqualTo(BackpackResult.Busy));
        Assert.That(callbackUse, Is.EqualTo(BackpackResult.Busy));
        Assert.That(consumedLayoutRemoved, Is.True);
        Assert.That(notifications, Is.EqualTo(1));
        Assert.That(health.CurrentHealth, Is.EqualTo(2));
        Assert.That(foodA.UsesThisRound, Is.EqualTo(1));
        Assert.That(foodB.UsesThisRound, Is.Zero);
        Assert.That(items.InstanceCount, Is.EqualTo(2));
    }

    [Test]
    public void MedkitRemainsInSameCellsAndSecondUseInRoundIsRejected()
    {
        ItemInstance medkit = CreateItem(false);
        backpack.TryAddPlacement(medkit.InstanceId, 1, 1, true);
        IReadOnlyList<BackpackPlacement> before = backpack.GetPlacements();
        Assert.That(health.TakeDamage(2), Is.True);
        Assert.That(backpack.TryUseItem(medkit.InstanceId, health, out ItemUseResult first), Is.EqualTo(BackpackResult.Success));
        Assert.That(first, Is.EqualTo(ItemUseResult.Success));
        Assert.That(health.CurrentHealth, Is.EqualTo(3));
        Assert.That(health.TakeDamage(1), Is.True);
        Assert.That(backpack.TryUseItem(medkit.InstanceId, health, out ItemUseResult second), Is.EqualTo(BackpackResult.Success));
        Assert.That(second, Is.EqualTo(ItemUseResult.RoundLimitReached));
        Assert.That(medkit.UsesThisRound, Is.EqualTo(1));
        Assert.That(medkit.Owner, Is.EqualTo(ItemOwner.Backpack));
        Assert.That(health.CurrentHealth, Is.EqualTo(2));
        CollectionAssert.AreEqual(before, backpack.GetPlacements());
    }

    [Test]
    public void DeadPlayerCannotUseAndBackpackDoesNotSettleDeathLoss()
    {
        ItemInstance food = CreateItem(true);
        backpack.TryAddPlacement(food.InstanceId, 0, 0, false);
        Assert.That(health.TakeDamage(3), Is.True);
        Assert.That(backpack.TryUseItem(food.InstanceId, health, out ItemUseResult result), Is.EqualTo(BackpackResult.Success));
        Assert.That(result, Is.EqualTo(ItemUseResult.InvalidTarget));
        Assert.That(food.Owner, Is.EqualTo(ItemOwner.Backpack));
        Assert.That(food.UsesThisRound, Is.Zero);
        Assert.That(backpack.GetPlacements().Count, Is.EqualTo(1));
        Assert.That(health.IsDead, Is.True);
    }

    [Test]
    public void ExternalSpaceQueryFindsTheFirstCellByRowWithoutMovingExistingItems()
    {
        AddQueryBlocker(1, 4, 0, 0);
        AddQueryBlocker(1, 1, 1, 0);
        ItemInstance external = CreateQueryItem(2, 1);
        AssertSpaceQuery(external.InstanceId, false, BackpackResult.Success, new Vector2Int(2, 0), false);
    }

    [TestCase(false, 2)]
    [TestCase(true, 1)]
    public void ExternalSpaceQueryExhaustsPreferredDirectionBeforeEarlierRotatedSpace(bool preferredRotated, int expectedX)
    {
        AddQueryBlocker(1, 1, preferredRotated ? 0 : 1, preferredRotated ? 1 : 0);
        ItemInstance external = CreateQueryItem(2, 1);
        Assert.That(backpack.CanPlaceExternal(external.InstanceId, 0, 0, !preferredRotated), Is.EqualTo(BackpackResult.Success));
        AssertSpaceQuery(external.InstanceId, preferredRotated, BackpackResult.Success,
            new Vector2Int(expectedX, 0), preferredRotated);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ExternalSpaceQueryUsesOtherDirectionOnlyWhenPreferredCannotFit(bool preferredRotated)
    {
        if (preferredRotated)
        {
            AddQueryBlocker(3, 4, 1, 0);
        }
        else
        {
            AddQueryBlocker(4, 3, 0, 1);
        }
        ItemInstance external = CreateQueryItem(1, 2);
        AssertSpaceQuery(external.InstanceId, preferredRotated, BackpackResult.Success,
            Vector2Int.zero, !preferredRotated);
    }

    [Test]
    public void ExternalSpaceQueryCanRotateAnOversizedPreferredDirectionInANonSquareBackpack()
    {
        backpack = CreateObject("Non-square query backpack").AddComponent<BackpackManager>();
        backpack.config = CreateAsset<BackpackConfigSO>();
        backpack.config.width = 3;
        backpack.config.height = 5;
        Assert.That(backpack.Initialize(items), Is.True);
        ItemInstance external = CreateQueryItem(4, 2);
        AssertSpaceQuery(external.InstanceId, false, BackpackResult.Success, Vector2Int.zero, true);
    }

    [Test]
    public void ExternalSpaceQueryDoesNotRotateAnItemWithoutPermission()
    {
        AddQueryBlocker(4, 3, 0, 1);
        ItemInstance external = CreateQueryItem(1, 2, false);
        AssertSpaceQuery(external.InstanceId, false, BackpackResult.Overlap, new Vector2Int(-1, -1), false);
        AssertSpaceQuery(external.InstanceId, true, BackpackResult.RotationNotAllowed, new Vector2Int(-1, -1), true);
    }

    [Test]
    public void ExternalSpaceQueryRejectsFullBackpackWithoutRemovingItsBlocker()
    {
        AddQueryBlocker(4, 4, 0, 0);
        ItemInstance external = CreateQueryItem(1, 1);
        AssertSpaceQuery(external.InstanceId, false, BackpackResult.Overlap, new Vector2Int(-1, -1), false);
    }

    [TestCase(5, 2)]
    [TestCase(int.MaxValue, 1)]
    public void ExternalSpaceQueryRejectsDimensionsTooLargeInEveryDirection(int width, int height)
    {
        ItemInstance external = CreateQueryItem(width, height);
        AssertSpaceQuery(external.InstanceId, false, BackpackResult.OutOfBounds, new Vector2Int(-1, -1), false);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("unknown-instance")]
    public void ExternalSpaceQueryRejectsMissingIdentity(string instanceId)
    {
        AddQueryBlocker(1, 1, 0, 0);
        AssertSpaceQuery(instanceId, false, BackpackResult.NotFound, new Vector2Int(-1, -1), false);
    }

    [Test]
    public void ExternalSpaceQueryRejectsUninitializedManager()
    {
        ItemInstance external = CreateQueryItem(1, 1);
        backpack = CreateObject("Uninitialized query backpack").AddComponent<BackpackManager>();
        AssertSpaceQuery(external.InstanceId, false, BackpackResult.NotReady, new Vector2Int(-1, -1), false);
    }

    [TestCase(ItemOwner.Lost)]
    [TestCase(ItemOwner.PendingRecovery)]
    [TestCase(ItemOwner.Consumed)]
    public void ExternalSpaceQueryRejectsInactiveOwnership(ItemOwner owner)
    {
        ItemInstance item;
        if (owner == ItemOwner.Consumed)
        {
            item = CreateItem(true);
            Assert.That(health.TakeDamage(1), Is.True);
            Assert.That(items.TryUseItem(item.InstanceId, health), Is.EqualTo(ItemUseResult.Success));
        }
        else
        {
            item = CreateQueryItem(1, 1, true, ItemOwner.Backpack, backpack.ContainerId,
                owner == ItemOwner.PendingRecovery);
            Assert.That(items.RecordLoss(item.InstanceId, ItemLossReason.BackpackDiscard), Is.True);
        }
        Assert.That(item.Owner, Is.EqualTo(owner));
        AssertSpaceQuery(item.InstanceId, false, BackpackResult.WrongOwner, new Vector2Int(-1, -1), false);
    }

    [Test]
    public void ExternalSpaceQueryRejectsWrongBackpackAndDuplicatePlacement()
    {
        ItemInstance wrongBackpack = CreateQueryItem(1, 1, true, ItemOwner.Backpack, "other-backpack");
        AssertSpaceQuery(wrongBackpack.InstanceId, false, BackpackResult.WrongContainer, new Vector2Int(-1, -1), false);
        ItemInstance external = CreateQueryItem(1, 1);
        Assert.That(backpack.TryAddPlacement(external.InstanceId, 0, 0, false), Is.EqualTo(BackpackResult.Success));
        AssertSpaceQuery(external.InstanceId, false, BackpackResult.DuplicateInstance, new Vector2Int(-1, -1), false);
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void ExternalSpaceQueryRejectsInvalidDefinitionDimensions(int width)
    {
        ItemInstance external = CreateQueryItem(1, 1);
        external.Definition.width = width;
        AssertSpaceQuery(external.InstanceId, false, BackpackResult.InvalidConfiguration, new Vector2Int(-1, -1), false);
    }

    [Test]
    public void ExternalSpaceQueryPreservesUsedUniqueAndRetainedInstances()
    {
        ItemInstance medkit = CreateItem(false);
        Assert.That(backpack.TryAddPlacement(medkit.InstanceId, 0, 0, false), Is.EqualTo(BackpackResult.Success));
        Assert.That(health.TakeDamage(1), Is.True);
        Assert.That(backpack.TryUseItem(medkit.InstanceId, health, out ItemUseResult used), Is.EqualTo(BackpackResult.Success));
        Assert.That(used, Is.EqualTo(ItemUseResult.Success));
        Assert.That(items.TrySetRetained(medkit.InstanceId, true), Is.True);
        ItemInstance unique = CreateQueryItem(1, 1, true, ItemOwner.Backpack, backpack.ContainerId, true);
        Assert.That(backpack.TryAddPlacement(unique.InstanceId, 3, 3, false), Is.EqualTo(BackpackResult.Success));
        Assert.That(items.TrySetRetained(unique.InstanceId, true), Is.True);
        ItemInstance external = CreateQueryItem(1, 2, true, ItemOwner.Storage, "warehouse");
        AssertSpaceQuery(external.InstanceId, false, BackpackResult.Success, new Vector2Int(2, 0), false);
        AssertSpaceQuery(medkit.InstanceId, false, BackpackResult.DuplicateInstance, new Vector2Int(-1, -1), false);
    }

    [Test]
    public void ExternalSpaceQueryRejectsBusyNotificationWithoutPublishingAnotherChange()
    {
        ItemInstance external = CreateQueryItem(1, 2, true, ItemOwner.World, "ground");
        string before = ItemQuerySnapshot();
        int notifications = 0;
        BackpackResult result = BackpackResult.Success;
        Vector2Int cell = Vector2Int.zero;
        bool rotated = false;
        backpack.Changed += () =>
        {
            notifications++;
            result = backpack.TryFindSpaceForExternal(external.InstanceId, true, out cell, out rotated);
        };
        backpack.NotifyChanged();
        Assert.That(result, Is.EqualTo(BackpackResult.Busy));
        Assert.That(cell, Is.EqualTo(new Vector2Int(-1, -1)));
        Assert.That(rotated, Is.True);
        Assert.That(notifications, Is.EqualTo(1));
        Assert.That(backpack.GetPlacements().Count, Is.Zero);
        Assert.That(ItemQuerySnapshot(), Is.EqualTo(before));
    }

    private void AssertSpaceQuery(string instanceId, bool preferredRotated, BackpackResult expected,
        Vector2Int expectedCell, bool expectedRotated)
    {
        IReadOnlyList<BackpackPlacement> placements = backpack.GetPlacements();
        List<ItemInstance> identities = items.GetInstances();
        string before = ItemQuerySnapshot();
        int notifications = 0;
        Action changed = () => notifications++;
        backpack.Changed += changed;
        try
        {
            BackpackResult result = backpack.TryFindSpaceForExternal(instanceId, preferredRotated,
                out Vector2Int cell, out bool rotated);
            Assert.That(result, Is.EqualTo(expected));
            Assert.That(cell, Is.EqualTo(expectedCell));
            Assert.That(rotated, Is.EqualTo(expectedRotated));
            if (result == BackpackResult.Success)
            {
                Assert.That(backpack.CanPlaceExternal(instanceId, cell.x, cell.y, rotated), Is.EqualTo(BackpackResult.Success));
            }
        }
        finally
        {
            backpack.Changed -= changed;
        }
        CollectionAssert.AreEqual(placements, backpack.GetPlacements());
        CollectionAssert.AreEqual(identities, items.GetInstances());
        Assert.That(ItemQuerySnapshot(), Is.EqualTo(before));
        Assert.That(notifications, Is.Zero);
    }

    private string ItemQuerySnapshot()
    {
        List<string> records = new List<string>();
        foreach (ItemInstance item in items.GetInstances())
        {
            records.Add(item.InstanceId + ":" + item.DefinitionId + ":" + item.UniqueId + ":" + item.Owner
                + ":" + item.ContainerId + ":" + item.SourceId + ":" + item.OriginalSpawnPointId
                + ":" + item.UseRound + ":" + item.UsesThisRound + ":" + item.IsRetained
                + ":" + item.RecoveryRound + ":" + item.LastLossReason + ":" + item.SpawnClaimed
                + ":" + item.Definition.width + ":" + item.Definition.height + ":" + item.Definition.canRotate
                + ":" + item.Definition.ev);
        }
        return items.CurrentRound + ":" + items.InstanceCount + "|" + string.Join("|", records);
    }

    private void AddQueryBlocker(int width, int height, int x, int y)
    {
        ItemInstance blocker = CreateQueryItem(width, height, true, ItemOwner.Backpack, backpack.ContainerId);
        Assert.That(backpack.TryAddPlacement(blocker.InstanceId, x, y, false), Is.EqualTo(BackpackResult.Success));
    }

    private ItemInstance CreateQueryItem(int width, int height, bool canRotate = true,
        ItemOwner owner = ItemOwner.Container, string container = "test-box", bool unique = false)
    {
        ItemDataSO definition = CreateAsset<ItemDataSO>();
        definition.definitionId = "backpack-space-test-" + nextDefinition++;
        definition.nameZh = "找位测试物品";
        definition.nameEn = "Space query item";
        definition.descriptionZh = "验证只读找空位。";
        definition.descriptionEn = "Verifies a read-only free space query.";
        definition.nameKey = definition.definitionId + ".name";
        definition.descriptionKey = definition.definitionId + ".description";
        definition.width = width;
        definition.height = height;
        definition.canRotate = canRotate;
        definition.ev = 2;
        definition.isUnique = unique;
        definition.uniqueId = unique ? definition.definitionId + ".unique" : string.Empty;
        Assert.That(items.TryCreateInstance(definition, owner, container, "test-source",
            "test-source/" + definition.definitionId, out ItemInstance item), Is.True);
        return item;
    }

    private ItemInstance CreateItem(bool consumed, ItemOwner owner = ItemOwner.Backpack, string container = "player-backpack")
    {
        ItemDataSO definition = CreateAsset<ItemDataSO>();
        definition.definitionId = "backpack-manager-test-" + nextDefinition++;
        definition.nameZh = "测试物品";
        definition.nameEn = "Test item";
        definition.descriptionZh = "测试治疗物品";
        definition.descriptionEn = "Test healing item";
        definition.nameKey = definition.definitionId + ".name";
        definition.descriptionKey = definition.definitionId + ".description";
        definition.width = 2;
        definition.height = 2;
        definition.canUse = true;
        definition.consumedOnUse = consumed;
        definition.usesPerRound = consumed ? 0 : 1;
        HealEffectSO effect = CreateAsset<HealEffectSO>();
        effect.healToFull = !consumed;
        effect.amount = 1;
        definition.effects.Add(effect);
        Assert.That(items.TryCreateInstance(definition, owner, container, "test", "", out ItemInstance item), Is.True);
        return item;
    }

    private GameObject CreateObject(string objectName)
    {
        GameObject result = new GameObject(objectName);
        createdObjects.Add(result);
        return result;
    }

    private T CreateAsset<T>() where T : ScriptableObject
    {
        T result = ScriptableObject.CreateInstance<T>();
        createdObjects.Add(result);
        return result;
    }
}
