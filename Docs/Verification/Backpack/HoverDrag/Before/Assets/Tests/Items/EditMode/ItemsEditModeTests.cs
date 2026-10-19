using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class ItemsEditModeTests
{
    private readonly List<UnityEngine.Object> createdObjects = new List<UnityEngine.Object>();
    private ItemManager manager;

    [SetUp]
    public void SetUp()
    {
        manager = CreateManager();
        Assert.That(manager.BeginRound(1), Is.True);
    }

    [TearDown]
    public void TearDown()
    {
        for (int i = createdObjects.Count - 1; i >= 0; i--)
        {
            if (createdObjects[i] != null)
            {
                UnityEngine.Object.DestroyImmediate(createdObjects[i]);
            }
        }
        createdObjects.Clear();
    }

    [Test]
    public void TwoFoodInstancesRemainIndependentAndNeverWriteRuntimeStateIntoDefinitions()
    {
        ItemDataSO food = CreateFood();
        ItemLibrarySO library = CreateAsset<ItemLibrarySO>();
        library.itemLibraryList.Add(new ItemLibraryEntry { itemData = food });
        manager.itemLibrary = library;
        string definitionBefore = JsonUtility.ToJson(food);
        string effectBefore = JsonUtility.ToJson(food.effects[0]);
        string libraryBefore = JsonUtility.ToJson(library);

        ItemInstance first = CreateInstance(food);
        ItemInstance second = CreateInstance(food);
        Assert.That(first.InstanceId, Is.Not.EqualTo(second.InstanceId));
        Assert.That(first.Definition, Is.SameAs(second.Definition));
        Assert.That(manager.TrySetOwner(second.InstanceId, ItemOwner.Storage, "storage"), Is.True);
        Assert.That(manager.TrySetRetained(second.InstanceId, true), Is.True);

        TestHealth health = new TestHealth(2, 5);
        Assert.That(manager.TryUseItem(first.InstanceId, health), Is.EqualTo(ItemUseResult.Success));
        Assert.That(health.CurrentHealth, Is.EqualTo(3));
        Assert.That(first.Owner, Is.EqualTo(ItemOwner.Consumed));
        Assert.That(first.UsesThisRound, Is.EqualTo(1));
        Assert.That(second.Owner, Is.EqualTo(ItemOwner.Storage));
        Assert.That(second.ContainerId, Is.EqualTo("storage"));
        Assert.That(second.IsRetained, Is.True);
        Assert.That(second.UsesThisRound, Is.Zero);
        Assert.That(manager.InstanceCount, Is.EqualTo(2));
        Assert.That(manager.TryUseItem(first.InstanceId, health), Is.EqualTo(ItemUseResult.NotCarried));
        Assert.That(health.CurrentHealth, Is.EqualTo(3));
        Assert.That(JsonUtility.ToJson(food), Is.EqualTo(definitionBefore));
        Assert.That(JsonUtility.ToJson(food.effects[0]), Is.EqualTo(effectBefore));
        Assert.That(JsonUtility.ToJson(library), Is.EqualTo(libraryBefore));
    }

    [TestCase(ItemOwner.World)]
    [TestCase(ItemOwner.Container)]
    [TestCase(ItemOwner.Backpack)]
    [TestCase(ItemOwner.Storage)]
    public void UniqueRegistrationRejectsDuplicatesInEveryActiveOwner(ItemOwner owner)
    {
        ItemDataSO doll = CreateUnique("doll");
        ItemInstance original = CreateInstance(doll, owner);
        bool claimedBeforeQuery = original.SpawnClaimed;

        Assert.That(manager.IsUniqueRegistered(doll.uniqueId), Is.True);
        Assert.That(manager.TryGetUniqueInstance(doll.uniqueId, out ItemInstance queried), Is.True);
        Assert.That(queried, Is.SameAs(original));
        Assert.That(original.SpawnClaimed, Is.EqualTo(claimedBeforeQuery));
        Assert.That(manager.TryCreateInstance(doll, ItemOwner.World, "", "second-source",
            "other-point", out ItemInstance duplicate), Is.False);
        Assert.That(duplicate, Is.Null);
        Assert.That(manager.InstanceCount, Is.EqualTo(1));

        if (owner == ItemOwner.Backpack || owner == ItemOwner.Storage)
        {
            Assert.That(manager.TryClaimUniqueSpawn(original.InstanceId, "ruins.point01"), Is.False);
        }
    }

    [TestCase(ItemOwner.World)]
    [TestCase(ItemOwner.Container)]
    public void InitialUniqueMapSpawnHasOnePermitAndQueriesDoNotGrantAnother(ItemOwner owner)
    {
        ItemInstance doll = CreateInstance(CreateUnique("doll"), owner);
        Assert.That(doll.SpawnClaimed, Is.False);
        Assert.That(manager.TryClaimUniqueSpawn(doll.InstanceId, "wrong-point"), Is.False);
        Assert.That(doll.SpawnClaimed, Is.False);
        Assert.That(manager.TryClaimUniqueSpawn(doll.InstanceId, "ruins.point01"), Is.True);
        Assert.That(manager.TryGetUniqueInstance(doll.UniqueId, out ItemInstance queried), Is.True);
        Assert.That(queried, Is.SameAs(doll));
        Assert.That(manager.TryClaimUniqueSpawn(queried.InstanceId, "ruins.point01"), Is.False);
        Assert.That(manager.BeginRound(2), Is.True);
        Assert.That(manager.TryClaimUniqueSpawn(doll.InstanceId, "ruins.point01"), Is.False);
    }

    [TestCase(ItemLossReason.ExplorationDeath)]
    [TestCase(ItemLossReason.ShelterAbandonment)]
    [TestCase(ItemLossReason.LeftBehindOnExtraction)]
    [TestCase(ItemLossReason.BackpackDiscard)]
    public void UniqueLossRecoversTheSameIdentityAtOriginalPointOnceNextRound(ItemLossReason reason)
    {
        ItemDataSO definition = CreateUnique("doll");
        ItemInstance doll = CreateInstance(definition);
        string originalId = doll.InstanceId;
        Assert.That(manager.TrySetRetained(originalId, true), Is.True);

        Assert.That(manager.RecordLoss(originalId, reason), Is.True);
        Assert.That(doll.Owner, Is.EqualTo(ItemOwner.PendingRecovery));
        Assert.That(doll.IsRetained, Is.False);
        Assert.That(doll.LastLossReason, Is.EqualTo(reason));
        Assert.That(doll.RecoveryRound, Is.EqualTo(2));
        Assert.That(doll.SourceId, Is.EqualTo("test-source"));
        Assert.That(doll.OriginalSpawnPointId, Is.EqualTo("ruins.point01"));
        Assert.That(manager.GetRecoverableItems(), Is.Empty);
        Assert.That(manager.RecordLoss(originalId, reason), Is.False);
        Assert.That(doll.RecoveryRound, Is.EqualTo(2));
        Assert.That(manager.TryClaimUniqueSpawn(originalId, "ruins.point01"), Is.False);
        Assert.That(manager.TrySetOwner(originalId, ItemOwner.Backpack, "backpack"), Is.False);
        Assert.That(manager.TryCreateInstance(definition, ItemOwner.World, "", "source",
            "ruins.point01", out _), Is.False);

        Assert.That(manager.BeginRound(2), Is.True);
        Assert.That(manager.RecordLoss(originalId, reason), Is.False);
        Assert.That(manager.GetRecoverableItems(), Is.EquivalentTo(new[] { doll }));
        Assert.That(manager.TryClaimUniqueSpawn(originalId, "wrong-point"), Is.False);
        Assert.That(doll.IsPendingRecovery, Is.True);
        Assert.That(manager.TryClaimUniqueSpawn(originalId, "ruins.point01"), Is.True);
        Assert.That(doll.InstanceId, Is.EqualTo(originalId));
        Assert.That(doll.Owner, Is.EqualTo(ItemOwner.World));
        Assert.That(doll.ContainerId, Is.EqualTo("ruins.point01"));
        Assert.That(doll.IsRetained, Is.False);
        Assert.That(manager.GetRecoverableItems(), Is.Empty);
        Assert.That(manager.TryClaimUniqueSpawn(originalId, "ruins.point01"), Is.False);
        Assert.That(manager.InstanceCount, Is.EqualTo(1));
    }

    [Test]
    public void WorldOwnershipTransferRemainsPickableButOrdinaryDiscardNeverGetsUniqueRecovery()
    {
        ItemInstance doll = CreateInstance(CreateUnique("doll"));
        Assert.That(manager.TrySetRetained(doll.InstanceId, true), Is.True);
        Assert.That(manager.TrySetOwner(doll.InstanceId, ItemOwner.World, "drop-point"), Is.True);
        Assert.That(doll.IsPendingRecovery, Is.False);
        Assert.That(doll.IsRetained, Is.True);
        Assert.That(doll.OriginalSpawnPointId, Is.EqualTo("ruins.point01"));
        Assert.That(manager.TrySetOwner(doll.InstanceId, ItemOwner.Backpack, "backpack"), Is.True);

        ItemInstance food = CreateInstance(CreateFood());
        Assert.That(manager.TrySetRetained(food.InstanceId, true), Is.True);
        Assert.That(manager.RecordLoss(food.InstanceId, ItemLossReason.BackpackDiscard), Is.True);
        Assert.That(food.Owner, Is.EqualTo(ItemOwner.Lost));
        Assert.That(food.IsRetained, Is.False);
        Assert.That(food.LastLossReason, Is.EqualTo(ItemLossReason.BackpackDiscard));
        Assert.That(manager.TryGetInstance(food.InstanceId, out ItemInstance registered), Is.True);
        Assert.That(registered, Is.SameAs(food));
        Assert.That(manager.BeginRound(2), Is.True);
        Assert.That(manager.GetRecoverableItems(), Is.Empty);
        Assert.That(manager.TryClaimUniqueSpawn(food.InstanceId, "ruins.point01"), Is.False);
        Assert.That(manager.TrySetOwner(food.InstanceId, ItemOwner.Backpack, "backpack"), Is.False);
    }

    [Test]
    public void MedkitHealsToFullKeepsIdentityAndRetentionAndRejectsSecondUse()
    {
        ItemInstance medkit = CreateInstance(CreateMedkit());
        string originalId = medkit.InstanceId;
        Assert.That(manager.TrySetRetained(originalId, true), Is.True);
        TestHealth health = new TestHealth(1, 5);
        health.DuringHealing = () =>
        {
            Assert.That(manager.TryUseItem(originalId, health), Is.EqualTo(ItemUseResult.Busy));
            Assert.That(manager.TrySetOwner(originalId, ItemOwner.Storage, "storage"), Is.False);
            Assert.That(manager.BeginRound(2), Is.False);
        };

        Assert.That(manager.TryUseItem(originalId, health), Is.EqualTo(ItemUseResult.Success));
        Assert.That(health.CurrentHealth, Is.EqualTo(5));
        Assert.That(medkit.InstanceId, Is.EqualTo(originalId));
        Assert.That(medkit.Owner, Is.EqualTo(ItemOwner.Backpack));
        Assert.That(medkit.IsRetained, Is.True);
        Assert.That(medkit.UsesThisRound, Is.EqualTo(1));
        health.CurrentHealth = 2;
        Assert.That(manager.TryUseItem(originalId, health), Is.EqualTo(ItemUseResult.RoundLimitReached));
        Assert.That(health.CurrentHealth, Is.EqualTo(2));
        Assert.That(medkit.UsesThisRound, Is.EqualTo(1));
        Assert.That(medkit.IsRetained, Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void FullHealthRejectsFoodAndMedkitWithoutConsumingOrSpendingQuota(bool useMedkit)
    {
        ItemInstance item = CreateInstance(useMedkit ? CreateMedkit() : CreateFood());
        Assert.That(manager.TrySetRetained(item.InstanceId, true), Is.True);
        TestHealth health = new TestHealth(5, 5);
        Assert.That(manager.TryUseItem(item.InstanceId, health), Is.EqualTo(ItemUseResult.FullHealth));
        Assert.That(health.HealCalls, Is.Zero);
        Assert.That(item.Owner, Is.EqualTo(ItemOwner.Backpack));
        Assert.That(item.IsRetained, Is.True);
        Assert.That(item.UsesThisRound, Is.Zero);

        health.CurrentHealth = 4;
        Assert.That(manager.TryUseItem(item.InstanceId, health), Is.EqualTo(ItemUseResult.Success));
        Assert.That(health.CurrentHealth, Is.EqualTo(5));
        Assert.That(item.UsesThisRound, Is.EqualTo(1));
        Assert.That(item.Owner, Is.EqualTo(useMedkit ? ItemOwner.Backpack : ItemOwner.Consumed));
        Assert.That(item.IsRetained, Is.EqualTo(useMedkit));
    }

    [TestCase(ItemOwner.World)]
    [TestCase(ItemOwner.Container)]
    [TestCase(ItemOwner.Storage)]
    public void HealingRequiresBackpackAndFailedOwnerCheckDoesNotSpendQuota(ItemOwner owner)
    {
        ItemInstance medkit = CreateInstance(CreateMedkit(), owner);
        TestHealth health = new TestHealth(1, 5);
        Assert.That(manager.TryUseItem(medkit.InstanceId, health), Is.EqualTo(ItemUseResult.NotCarried));
        Assert.That(health.HealCalls, Is.Zero);
        Assert.That(medkit.UsesThisRound, Is.Zero);
        Assert.That(medkit.Owner, Is.EqualTo(owner));
        Assert.That(manager.TrySetOwner(medkit.InstanceId, ItemOwner.Backpack, "backpack"), Is.True);
        Assert.That(manager.TryUseItem(medkit.InstanceId, health), Is.EqualTo(ItemUseResult.Success));
    }

    [Test]
    public void InvalidHealthTargetsNeverMutateOrConsumeTheItem()
    {
        ItemInstance food = CreateInstance(CreateFood());
        IHealthTarget[] targets =
        {
            null,
            new TestHealth(0, 5),
            new TestHealth(-1, 5),
            new TestHealth(6, 5),
            new TestHealth(1, 0)
        };
        foreach (IHealthTarget target in targets)
        {
            Assert.That(manager.TryUseItem(food.InstanceId, target), Is.EqualTo(ItemUseResult.InvalidTarget));
            Assert.That(food.Owner, Is.EqualTo(ItemOwner.Backpack));
            Assert.That(food.UsesThisRound, Is.Zero);
            if (target is TestHealth health)
            {
                Assert.That(health.HealCalls, Is.Zero);
            }
        }
    }

    [Test]
    public void QueriesTransfersAndDisplaySelectionKeepQuotaAndOnlyNewRoundRefreshesIt()
    {
        ItemDataSO definition = CreateMedkit();
        ItemInstance medkit = CreateInstance(definition);
        TestHealth health = new TestHealth(1, 5);
        Assert.That(manager.TrySetRetained(medkit.InstanceId, true), Is.True);
        Assert.That(manager.TryUseItem(medkit.InstanceId, health), Is.EqualTo(ItemUseResult.Success));
        Assert.That(manager.TrySetOwner(medkit.InstanceId, ItemOwner.Storage, "storage"), Is.True);
        Assert.That(manager.TrySetOwner(medkit.InstanceId, ItemOwner.World, "temporary-drop"), Is.True);
        Assert.That(manager.TrySetOwner(medkit.InstanceId, ItemOwner.Backpack, "backpack"), Is.True);
        Assert.That(manager.TryGetInstance(medkit.InstanceId, out ItemInstance queried), Is.True);
        Assert.That(queried, Is.SameAs(medkit));
        manager.GetInstances().Clear();
        Assert.That(manager.InstanceCount, Is.EqualTo(1));
        Assert.That(definition.nameZh, Is.Not.Empty);
        Assert.That(definition.nameEn, Is.Not.Empty);
        Assert.That(medkit.UsesThisRound, Is.EqualTo(1));
        Assert.That(medkit.IsRetained, Is.True);
        Assert.That(manager.BeginRound(1), Is.False);
        Assert.That(manager.BeginRound(0), Is.False);
        health.CurrentHealth = 2;
        Assert.That(manager.TryUseItem(medkit.InstanceId, health), Is.EqualTo(ItemUseResult.RoundLimitReached));

        Assert.That(manager.BeginRound(2), Is.True);
        Assert.That(medkit.UseRound, Is.EqualTo(2));
        Assert.That(medkit.UsesThisRound, Is.Zero);
        Assert.That(manager.TryUseItem(medkit.InstanceId, health), Is.EqualTo(ItemUseResult.Success));
        Assert.That(manager.BeginRound(2), Is.False);
        Assert.That(manager.BeginRound(1), Is.False);
        Assert.That(medkit.UsesThisRound, Is.EqualTo(1));
        Assert.That(medkit.IsRetained, Is.True);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void MultipleSupportedHealEffectsCountAsOneUseEvenWhenAnEarlierEffectFillsHealth(bool fullFirst)
    {
        ItemDataSO food = CreateFood();
        food.effects.Clear();
        HealEffectSO full = CreateHeal(true, 1);
        HealEffectSO fixedAmount = CreateHeal(false, 1);
        food.effects.Add(fullFirst ? full : fixedAmount);
        food.effects.Add(fullFirst ? fixedAmount : full);
        ItemInstance instance = CreateInstance(food);
        TestHealth health = new TestHealth(1, 5);

        Assert.That(manager.TryUseItem(instance.InstanceId, health), Is.EqualTo(ItemUseResult.Success));
        Assert.That(health.CurrentHealth, Is.EqualTo(5));
        Assert.That(health.HealCalls, Is.EqualTo(fullFirst ? 1 : 2));
        Assert.That(instance.Owner, Is.EqualTo(ItemOwner.Consumed));
        Assert.That(instance.UsesThisRound, Is.EqualTo(1));
    }

    [TestCase("unsupported")]
    [TestCase("null")]
    [TestCase("invalid-heal")]
    public void InvalidLaterEffectIsRejectedBeforeAnyEarlierHealingRuns(string invalidKind)
    {
        ItemDataSO food = CreateFood();
        ItemInstance instance = CreateInstance(food);
        UnsupportedItemEffectSO unsupported = null;
        if (invalidKind == "unsupported")
        {
            unsupported = CreateAsset<UnsupportedItemEffectSO>();
            food.effects.Add(unsupported);
        }
        else if (invalidKind == "null")
        {
            food.effects.Add(null);
        }
        else
        {
            food.effects.Add(CreateHeal(false, 0));
        }
        TestHealth health = new TestHealth(1, 5);

        Assert.That(manager.TryUseItem(instance.InstanceId, health), Is.EqualTo(ItemUseResult.InvalidConfiguration));
        Assert.That(health.CurrentHealth, Is.EqualTo(1));
        Assert.That(health.HealCalls, Is.Zero);
        Assert.That(instance.Owner, Is.EqualTo(ItemOwner.Backpack));
        Assert.That(instance.UsesThisRound, Is.Zero);
        if (unsupported != null)
        {
            Assert.That(unsupported.ExecutionCount, Is.Zero);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void HealthTargetThatAppliesNoHealingDoesNotConsumeOrSpendQuota(bool useMedkit)
    {
        ItemInstance item = CreateInstance(useMedkit ? CreateMedkit() : CreateFood());
        Assert.That(manager.TrySetRetained(item.InstanceId, true), Is.True);
        TestHealth health = new TestHealth(1, 5) { IgnoreHealing = true };
        Assert.That(manager.TryUseItem(item.InstanceId, health), Is.EqualTo(ItemUseResult.NoHealingApplied));
        Assert.That(health.CurrentHealth, Is.EqualTo(1));
        Assert.That(health.HealCalls, Is.EqualTo(1));
        Assert.That(item.UsesThisRound, Is.Zero);
        Assert.That(item.Owner, Is.EqualTo(ItemOwner.Backpack));
        Assert.That(item.IsRetained, Is.True);
        health.IgnoreHealing = false;
        Assert.That(manager.TryUseItem(item.InstanceId, health), Is.EqualTo(ItemUseResult.Success));
    }

    [TestCase("width")]
    [TestCase("height")]
    [TestCase("search")]
    [TestCase("probability-low")]
    [TestCase("probability-high")]
    [TestCase("probability-nan")]
    [TestCase("definition-id")]
    [TestCase("unique-id")]
    [TestCase("negative-limit")]
    [TestCase("missing-text-key")]
    public void InvalidInspectorConfigurationIsReportedAndCannotCreateInstances(string invalidField)
    {
        ItemDataSO food = CreateFood();
        switch (invalidField)
        {
            case "width": food.width = 0; break;
            case "height": food.height = -1; break;
            case "search": food.searchSeconds = -1f; break;
            case "probability-low": food.defaultSpawnProbability = -0.1f; break;
            case "probability-high": food.defaultSpawnProbability = 1.1f; break;
            case "probability-nan": food.defaultSpawnProbability = float.NaN; break;
            case "definition-id": food.definitionId = " "; break;
            case "unique-id": food.isUnique = true; food.consumedOnUse = false; food.uniqueId = ""; break;
            case "negative-limit": food.usesPerRound = -1; break;
            case "missing-text-key": food.nameKey = ""; break;
        }
        Assert.That(food.TryValidate(out string error), Is.False);
        Assert.That(error, Is.Not.Null.And.Not.Empty);
        Assert.That(manager.TryCreateInstance(food, ItemOwner.Backpack, "backpack", "source", "point", out _), Is.False);
        Assert.That(manager.InstanceCount, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void LibraryRejectsDuplicateDefinitionOrUniqueIdentifiers(bool duplicateUniqueId)
    {
        ItemLibrarySO library = CreateAsset<ItemLibrarySO>();
        ItemDataSO first = CreateUnique("doll");
        ItemDataSO second = CreateUnique("medkit");
        library.itemLibraryList.Add(new ItemLibraryEntry { itemData = first });
        library.itemLibraryList.Add(new ItemLibraryEntry { itemData = second });
        Assert.That(library.TryValidate(out _), Is.True);
        Assert.That(library.FindDefinition(first.definitionId), Is.SameAs(first));
        Assert.That(library.FindDefinition("missing"), Is.Null);
        if (duplicateUniqueId)
        {
            second.uniqueId = first.uniqueId;
        }
        else
        {
            second.definitionId = first.definitionId;
        }
        Assert.That(library.TryValidate(out string error), Is.False);
        Assert.That(error, Is.Not.Null.And.Not.Empty);
        CreateInstance(first);
        Assert.That(manager.TryCreateInstance(second, ItemOwner.World, "", "source", "point", out _), Is.False);
        Assert.That(manager.InstanceCount, Is.EqualTo(1));
    }

    [TestCase("definition-id")]
    [TestCase("unique-id")]
    [TestCase("unique-flag")]
    public void EditingRegisteredIdentityCannotCreateAnotherInstanceOrUseTheWrongIdentity(string changedField)
    {
        ItemDataSO definition = CreateMedkit();
        ItemInstance original = CreateInstance(definition);
        string originalDefinitionId = definition.definitionId;
        string originalUniqueId = definition.uniqueId;
        if (changedField == "definition-id")
        {
            definition.definitionId = "different-definition";
        }
        else if (changedField == "unique-id")
        {
            definition.uniqueId = "different-unique";
        }
        else
        {
            definition.isUnique = false;
        }

        TestHealth health = new TestHealth(1, 5);
        Assert.That(manager.TryCreateInstance(definition, ItemOwner.World, "", "source", "point", out _), Is.False);
        Assert.That(manager.TryUseItem(original.InstanceId, health), Is.EqualTo(ItemUseResult.InvalidConfiguration));
        Assert.That(original.DefinitionId, Is.EqualTo(originalDefinitionId));
        Assert.That(original.UniqueId, Is.EqualTo(originalUniqueId));
        Assert.That(manager.TryGetUniqueInstance(originalUniqueId, out ItemInstance queried), Is.True);
        Assert.That(queried, Is.SameAs(original));
        Assert.That(manager.InstanceCount, Is.EqualTo(1));
        Assert.That(original.UsesThisRound, Is.Zero);
        Assert.That(health.HealCalls, Is.Zero);

        definition.definitionId = originalDefinitionId;
        definition.uniqueId = originalUniqueId;
        definition.isUnique = true;
        Assert.That(manager.TryUseItem(original.InstanceId, health), Is.EqualTo(ItemUseResult.Success));
    }

    [Test]
    public void MissingUnusableAndNotStartedRequestsReturnSpecificFailuresWithoutHealing()
    {
        TestHealth health = new TestHealth(1, 5);
        Assert.That(manager.TryUseItem("missing", health), Is.EqualTo(ItemUseResult.NotFound));
        ItemInstance doll = CreateInstance(CreateUnique("doll"));
        Assert.That(manager.TryUseItem(doll.InstanceId, health), Is.EqualTo(ItemUseResult.NotUsable));
        manager = CreateManager();
        ItemInstance food = CreateInstance(CreateFood());
        Assert.That(manager.TryUseItem(food.InstanceId, health), Is.EqualTo(ItemUseResult.RoundNotStarted));
        Assert.That(health.HealCalls, Is.Zero);
        Assert.That(food.UsesThisRound, Is.Zero);
        Assert.That(food.Owner, Is.EqualTo(ItemOwner.Backpack));
        Assert.That(manager.BeginRound(1), Is.True);
        Assert.That(manager.TryUseItem(food.InstanceId, health), Is.EqualTo(ItemUseResult.Success));
    }

    [Test]
    public void FixedHealingClampsBeforeCallingTheHealthTarget()
    {
        ItemDataSO food = CreateFood();
        ((HealEffectSO)food.effects[0]).amount = int.MaxValue;
        ItemInstance instance = CreateInstance(food);
        TestHealth health = new TestHealth(4, 5);
        Assert.That(manager.TryUseItem(instance.InstanceId, health), Is.EqualTo(ItemUseResult.Success));
        Assert.That(health.LastHealAmount, Is.EqualTo(1));
        Assert.That(health.CurrentHealth, Is.EqualTo(5));
        Assert.That(instance.Owner, Is.EqualTo(ItemOwner.Consumed));
        Assert.That(instance.UsesThisRound, Is.EqualTo(1));
    }

    private ItemManager CreateManager()
    {
        GameObject module = new GameObject("ItemsEditModeTests_Module");
        createdObjects.Add(module);
        return module.AddComponent<ItemManager>();
    }

    private T CreateAsset<T>() where T : ScriptableObject
    {
        T asset = ScriptableObject.CreateInstance<T>();
        createdObjects.Add(asset);
        return asset;
    }

    private ItemDataSO CreateDefinition(string id)
    {
        ItemDataSO definition = CreateAsset<ItemDataSO>();
        definition.definitionId = id;
        definition.nameZh = "测试物品";
        definition.nameEn = "Test item";
        definition.descriptionZh = "仅用于物品模块自动测试。";
        definition.descriptionEn = "For item module tests only.";
        definition.nameKey = "items." + id + ".name";
        definition.descriptionKey = "items." + id + ".description";
        definition.width = 2;
        definition.height = 2;
        definition.defaultSpawnProbability = 1f;
        definition.searchSeconds = 1f;
        return definition;
    }

    private ItemDataSO CreateUnique(string id)
    {
        ItemDataSO definition = CreateDefinition(id);
        definition.isUnique = true;
        definition.uniqueId = "unique." + id;
        return definition;
    }

    private ItemDataSO CreateFood()
    {
        ItemDataSO definition = CreateDefinition("food");
        definition.canUse = true;
        definition.consumedOnUse = true;
        definition.effects.Add(CreateHeal(false, 1));
        return definition;
    }

    private ItemDataSO CreateMedkit()
    {
        ItemDataSO definition = CreateUnique("medkit");
        definition.canUse = true;
        definition.usesPerRound = 1;
        definition.effects.Add(CreateHeal(true, 1));
        return definition;
    }

    private HealEffectSO CreateHeal(bool healToFull, int amount)
    {
        HealEffectSO effect = CreateAsset<HealEffectSO>();
        effect.healToFull = healToFull;
        effect.amount = amount;
        return effect;
    }

    private ItemInstance CreateInstance(ItemDataSO definition, ItemOwner owner = ItemOwner.Backpack)
    {
        Assert.That(manager.TryCreateInstance(definition, owner, "test-container", "test-source",
            "ruins.point01", out ItemInstance instance), Is.True);
        return instance;
    }

    // 此对象仅验证治疗边界，不是02的生产玩家或生命系统。
    private sealed class TestHealth : IHealthTarget
    {
        public int CurrentHealth { get; set; }
        public int MaxHealth { get; }
        public bool IgnoreHealing { get; set; }
        public Action DuringHealing { get; set; }
        public int HealCalls { get; private set; }
        public int LastHealAmount { get; private set; }

        public TestHealth(int currentHealth, int maxHealth)
        {
            CurrentHealth = currentHealth;
            MaxHealth = maxHealth;
        }

        public void Heal(int amount)
        {
            HealCalls++;
            LastHealAmount = amount;
            DuringHealing?.Invoke();
            if (!IgnoreHealing)
            {
                CurrentHealth = Math.Min(MaxHealth, CurrentHealth + amount);
            }
        }
    }
}
