using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class LootGenerationTests
{
    private readonly List<UnityEngine.Object> created = new List<UnityEngine.Object>();
    private ItemManager items;

    [SetUp]
    public void SetUp()
    {
        GameObject root = new GameObject("LootGenerationTestItems");
        created.Add(root);
        items = root.AddComponent<ItemManager>();
        Assert.That(items.BeginRound(1), Is.True);
    }

    [TearDown]
    public void TearDown()
    {
        for (int i = created.Count - 1; i >= 0; i--)
        {
            if (created[i] != null) { UnityEngine.Object.DestroyImmediate(created[i]); }
        }
        created.Clear();
    }

    [TestCase(0f, 0f, false)]
    [TestCase(0f, 1f, false)]
    [TestCase(1f, 0f, true)]
    [TestCase(1f, 1f, true)]
    [TestCase(0.5f, 0.499f, true)]
    [TestCase(0.5f, 0.5f, false)]
    public void BoundariesAreExplicit(float probability, float sample, bool expected)
    {
        Assert.That(LootGenerator.IsHit(probability, sample), Is.EqualTo(expected));
    }

    [Test]
    public void EveryIntermediateCandidateGetsAnIndependentSampleInConfiguredOrder()
    {
        LootTableSO table = Table(0.3f, 0.8f, 0.4f, 0.9f);
        Queue<float> samples = new Queue<float>(new[] { 0.1f, 0.9f, 0.2f, 0.2f });
        Assert.That(LootGenerator.TrySelect(table, items, "map/box", out List<LootTableEntry> result,
            out string error, () => samples.Dequeue()), Is.True, error);
        CollectionAssert.AreEqual(new[] { "entry-0", "entry-2", "entry-3" }, result.ConvertAll(entry => entry.entryId));
        Assert.That(samples.Count, Is.Zero);
        Assert.That(items.InstanceCount, Is.Zero, "选择不应私自创建或登记身份。");
    }

    [Test]
    public void ZeroOneAndEmptyTablesDoNotNeedRandomSamples()
    {
        LootTableSO table = Table(0f, 1f, 1f, 0f);
        Assert.That(LootGenerator.TrySelect(table, items, "map/box", out List<LootTableEntry> result,
            out string error, () => throw new InvalidOperationException("边界不需要随机数")), Is.True, error);
        CollectionAssert.AreEqual(new[] { "entry-1", "entry-2" }, result.ConvertAll(entry => entry.entryId));
        table.entries.Clear();
        Assert.That(LootGenerator.TrySelect(table, items, "map/box", out result, out error), Is.True, error);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void OverrideReplacesDefaultInsteadOfMultiplyingIt()
    {
        LootTableSO table = Table(0f);
        table.entries[0].item.defaultSpawnProbability = 1f;
        table.entries[0].useDefaultProbability = false;
        Assert.That(LootGenerator.TrySelect(table, items, "map/box", out List<LootTableEntry> result, out _), Is.True);
        Assert.That(result, Is.Empty);
        table.entries[0].useDefaultProbability = true;
        Assert.That(LootGenerator.TrySelect(table, items, "map/box", out result, out _), Is.True);
        Assert.That(result.Count, Is.EqualTo(1));
        table.entries[0].item.defaultSpawnProbability = 0f;
        table.entries[0].overrideProbability = 1f;
        table.entries[0].useDefaultProbability = false;
        Assert.That(LootGenerator.TrySelect(table, items, "map/box", out result, out _), Is.True);
        Assert.That(result.Count, Is.EqualTo(1));
    }

    [Test]
    public void InvalidLaterCandidateRejectsWholeTableBeforeTakingSamples()
    {
        LootTableSO table = Table(0.5f, 0.5f);
        table.entries[1].item = null;
        int draws = 0;
        Assert.That(LootGenerator.TrySelect(table, items, "map/box", out List<LootTableEntry> result,
            out string error, () => { draws++; return 0f; }), Is.False);
        Assert.That(error, Does.Contain("entry-1"));
        Assert.That(draws, Is.Zero);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void DuplicateEntryIdAndNonFiniteProbabilityAreRejected()
    {
        LootTableSO table = Table(1f, 1f);
        table.entries[1].entryId = table.entries[0].entryId;
        Assert.That(table.TryValidate(out _), Is.False);
        table.entries[1].entryId = "different";
        table.entries[1].overrideProbability = float.NaN;
        Assert.That(table.TryValidate(out _), Is.False);
        table.entries[1].overrideProbability = 1.01f;
        Assert.That(table.TryValidate(out _), Is.False);
    }

    [TestCase(ItemOwner.Backpack)]
    [TestCase(ItemOwner.Storage)]
    public void UniqueAlreadyCarriedOrStoredIsNotSelectedEvenAtOneHundredPercent(ItemOwner owner)
    {
        LootTableSO table = Table(1f);
        ItemDataSO definition = table.entries[0].item;
        definition.isUnique = true;
        definition.uniqueId = "unique-test";
        Assert.That(items.TryCreateInstance(definition, owner, "carried", "map/box", "map/box/entry-0", out _), Is.True);
        Assert.That(LootGenerator.TrySelect(table, items, "map/box", out List<LootTableEntry> result, out _), Is.True);
        Assert.That(result, Is.Empty);
        Assert.That(items.InstanceCount, Is.EqualTo(1));
    }

    [Test]
    public void EligibleRecoveryBypassesZeroProbabilityAndKeepsTheRegisteredIdentity()
    {
        LootTableSO table = Table(0f);
        ItemDataSO definition = table.entries[0].item;
        definition.isUnique = true;
        definition.uniqueId = "unique-test";
        Assert.That(items.TryCreateInstance(definition, ItemOwner.Backpack, "pack", "map/box", "map/box/entry-0", out ItemInstance original), Is.True);
        Assert.That(items.RecordLoss(original.InstanceId, ItemLossReason.ExplorationDeath), Is.True);
        Assert.That(LootGenerator.TrySelect(table, items, "map/box", out List<LootTableEntry> beforeRound, out _), Is.True);
        Assert.That(beforeRound, Is.Empty);
        Assert.That(items.BeginRound(2), Is.True);
        Assert.That(LootGenerator.TrySelect(table, items, "map/box", out List<LootTableEntry> recovered,
            out string error, () => throw new InvalidOperationException("找回不抽概率")), Is.True, error);
        Assert.That(recovered.Count, Is.EqualTo(1));
        Assert.That(items.TryGetUniqueInstance(definition.uniqueId, out ItemInstance current), Is.True);
        Assert.That(current, Is.SameAs(original));
        Assert.That(current.Owner, Is.EqualTo(ItemOwner.PendingRecovery), "选择不能冒充领取生成许可。");
    }

    [TestCase(ItemOwner.World)]
    [TestCase(ItemOwner.Container)]
    public void ExistingUniqueAtItsSourceRemainsEligibleForRebinding(ItemOwner owner)
    {
        LootTableSO table = Table(0f);
        ItemDataSO definition = table.entries[0].item;
        definition.isUnique = true;
        definition.uniqueId = "unique-test";
        Assert.That(items.TryCreateInstance(definition, owner, "map/box", "map/box", "map/box/entry-0", out ItemInstance original), Is.True);
        Assert.That(items.TryClaimUniqueSpawn(original.InstanceId, original.OriginalSpawnPointId), Is.True);
        Assert.That(LootGenerator.TrySelect(table, items, "map/box", out List<LootTableEntry> result, out _), Is.True);
        Assert.That(result.Count, Is.EqualTo(1));
        Assert.That(items.InstanceCount, Is.EqualTo(1));
    }

    [Test]
    public void ConflictingUniqueOriginIsAnErrorEvenWhenChanceIsZero()
    {
        LootTableSO table = Table(0f);
        ItemDataSO definition = table.entries[0].item;
        definition.isUnique = true;
        definition.uniqueId = "unique-test";
        Assert.That(items.TryCreateInstance(definition, ItemOwner.Storage, "store", "map/original", "map/original/entry", out _), Is.True);
        Assert.That(LootGenerator.TrySelect(table, items, "map/box", out List<LootTableEntry> result, out string error), Is.False);
        Assert.That(result, Is.Empty);
        Assert.That(error, Does.Contain("原点冲突"));
    }

    private LootTableSO Table(params float[] probabilities)
    {
        LootTableSO table = ScriptableObject.CreateInstance<LootTableSO>();
        created.Add(table);
        for (int i = 0; i < probabilities.Length; i++)
        {
            ItemDataSO item = ScriptableObject.CreateInstance<ItemDataSO>();
            created.Add(item);
            item.definitionId = "definition-" + i;
            item.nameZh = "测试物品";
            item.nameEn = "Test item";
            item.descriptionZh = "测试说明";
            item.descriptionEn = "Test description";
            item.nameKey = item.definitionId + ".name";
            item.descriptionKey = item.definitionId + ".description";
            table.entries.Add(new LootTableEntry { entryId = "entry-" + i, item = item,
                useDefaultProbability = false, overrideProbability = probabilities[i] });
        }
        return table;
    }
}
