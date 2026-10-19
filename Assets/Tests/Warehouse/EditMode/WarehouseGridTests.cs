using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class WarehouseGridTests
{
    private readonly List<UnityEngine.Object> createdObjects = new List<UnityEngine.Object>();
    private ItemManager items;
    private int nextDefinition;

    [SetUp]
    public void SetUp()
    {
        GameObject root = new GameObject("仓库格子测试物品模块");
        createdObjects.Add(root);
        items = root.AddComponent<ItemManager>();
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
    public void DefaultConfigHasFourHundredCellsAndRejectsNonpositiveDimensions()
    {
        WarehouseConfigSO config = ScriptableObject.CreateInstance<WarehouseConfigSO>();
        createdObjects.Add(config);
        Assert.That(config.width, Is.EqualTo(20));
        Assert.That(config.height, Is.EqualTo(20));
        Assert.That(config.width * config.height, Is.EqualTo(400));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WarehouseGrid(0, 20));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WarehouseGrid(20, -1));
    }

    [Test]
    public void NonSquareGridRotatesWithoutChangingDefinitionAndRejectsOverflowCoordinates()
    {
        WarehouseGrid grid = new WarehouseGrid(3, 5);
        ItemInstance item = CreateItem(2, 3);
        Assert.That(grid.TryAdd(item, 0, 0, false), Is.EqualTo(WarehouseResult.Success));
        Assert.That(grid.TryMove(item.InstanceId, 0, 3, true), Is.EqualTo(WarehouseResult.Success));
        Assert.That(grid.TryGetPlacement(item.InstanceId, out WarehousePlacement original), Is.True);
        Assert.That(original.Width, Is.EqualTo(3));
        Assert.That(original.Height, Is.EqualTo(2));

        Assert.That(grid.TryMove(item.InstanceId, 1, 3, true), Is.EqualTo(WarehouseResult.OutOfBounds));
        Assert.That(grid.TryMove(item.InstanceId, 0, 4, true), Is.EqualTo(WarehouseResult.OutOfBounds));
        Assert.That(grid.TryMove(item.InstanceId, int.MaxValue, 0, false), Is.EqualTo(WarehouseResult.OutOfBounds));
        Assert.That(grid.TryMove(item.InstanceId, 0, int.MinValue, false), Is.EqualTo(WarehouseResult.OutOfBounds));
        Assert.That(grid.TryGetPlacement(item.InstanceId, out WarehousePlacement after), Is.True);
        Assert.That(after, Is.SameAs(original), "失败不能替换原摆放或朝向。");
        Assert.That(item.Definition.width, Is.EqualTo(2));
        Assert.That(item.Definition.height, Is.EqualTo(3));
    }

    [Test]
    public void MoveIgnoresOnlyOwnRectangleAndFailureKeepsEveryPlacement()
    {
        WarehouseGrid grid = new WarehouseGrid(4, 4);
        ItemInstance moving = CreateItem(2, 1);
        ItemInstance blocker = CreateItem(1, 2);
        Assert.That(grid.TryAdd(moving, 0, 0, false), Is.EqualTo(WarehouseResult.Success));
        Assert.That(grid.TryAdd(blocker, 3, 0, false), Is.EqualTo(WarehouseResult.Success));
        IReadOnlyList<WarehousePlacement> before = grid.GetPlacements();
        Assert.That(grid.CanMove(moving.InstanceId, 1, 0, false), Is.EqualTo(WarehouseResult.Success));
        Assert.That(grid.TryMove(moving.InstanceId, 2, 0, false), Is.EqualTo(WarehouseResult.Overlap));
        Assert.That(grid.TryMove(moving.InstanceId, 3, 0, true), Is.EqualTo(WarehouseResult.Overlap));
        CollectionAssert.AreEqual(before, grid.GetPlacements());

        Assert.That(grid.TryMove(moving.InstanceId, 1, 0, false), Is.EqualTo(WarehouseResult.Success));
        Assert.That(grid.TryGetPlacement(moving.InstanceId, out WarehousePlacement moved), Is.True);
        Assert.That(moved.X, Is.EqualTo(1));
        Assert.That(moved.Item, Is.SameAs(moving));
        Assert.That(grid.GetPlacements()[1], Is.SameAs(before[1]));
        Assert.That(before[0].X, Is.Zero, "外部保存的旧快照不能被后续移动改写。");
    }

    [Test]
    public void DuplicateInstanceForbiddenRotationAndInvalidDimensionsPreserveLayout()
    {
        WarehouseGrid grid = new WarehouseGrid(4, 4);
        ItemInstance item = CreateItem(1, 2, false);
        Assert.That(grid.TryAdd(item, 0, 0, false), Is.EqualTo(WarehouseResult.Success));
        IReadOnlyList<WarehousePlacement> before = grid.GetPlacements();
        Assert.That(grid.TryAdd(item, 2, 0, false), Is.EqualTo(WarehouseResult.DuplicateInstance));
        Assert.That(grid.TryMove(item.InstanceId, 0, 1, true), Is.EqualTo(WarehouseResult.RotationNotAllowed));
        ItemInstance invalid = CreateItem(1, 1);
        invalid.Definition.width = 0;
        Assert.That(grid.TryAdd(invalid, 2, 2, false), Is.EqualTo(WarehouseResult.InvalidConfiguration));
        CollectionAssert.AreEqual(before, grid.GetPlacements());
    }

    [Test]
    public void FragmentedFreeAreaDoesNotFitRectangleDespiteEnoughTotalCells()
    {
        WarehouseGrid grid = new WarehouseGrid(4, 4);
        for (int y = 1; y < 4; y += 2)
        {
            for (int x = 1; x < 4; x += 2)
            {
                Assert.That(grid.TryAdd(CreateItem(1, 1), x, y, false), Is.EqualTo(WarehouseResult.Success));
            }
        }
        IReadOnlyList<WarehousePlacement> before = grid.GetPlacements();
        ItemInstance square = CreateItem(2, 2);
        for (int y = 0; y < 3; y++)
        {
            for (int x = 0; x < 3; x++)
            {
                Assert.That(grid.TryAdd(square, x, y, false), Is.EqualTo(WarehouseResult.Overlap));
            }
        }
        Assert.That(16 - grid.Count, Is.EqualTo(12));
        CollectionAssert.AreEqual(before, grid.GetPlacements());
    }

    [Test]
    public void PreviewAndRemovalSnapshotsNeverModifyItemAuthority()
    {
        WarehouseGrid grid = new WarehouseGrid(4, 4);
        ItemInstance item = CreateItem(1, 2);
        Assert.That(items.TrySetRetained(item.InstanceId, true), Is.True);
        Assert.That(grid.TryAdd(item, 0, 0, false), Is.EqualTo(WarehouseResult.Success));
        IReadOnlyList<WarehousePlacement> before = grid.GetPlacements();
        Assert.That(grid.CanMove(item.InstanceId, 2, 3, true), Is.EqualTo(WarehouseResult.Success));
        Assert.That(grid.CanMove(item.InstanceId, 4, 3, true), Is.EqualTo(WarehouseResult.OutOfBounds));
        CollectionAssert.AreEqual(before, grid.GetPlacements());
        Assert.That(grid.TryRemove(item.InstanceId, out WarehousePlacement removed), Is.EqualTo(WarehouseResult.Success));
        Assert.That(removed, Is.SameAs(before[0]));
        Assert.That(before.Count, Is.EqualTo(1));
        Assert.That(grid.Count, Is.Zero);
        Assert.That(item.Owner, Is.EqualTo(ItemOwner.Storage));
        Assert.That(item.ContainerId, Is.EqualTo("shelter-warehouse"));
        Assert.That(item.IsRetained, Is.True);
        Assert.That(item.UsesThisRound, Is.Zero);
        Assert.That(grid.TryRemove(item.InstanceId, out _), Is.EqualTo(WarehouseResult.MissingPlacement));
    }

    [TestCase(0, 30)]
    [TestCase(30, -1)]
    [TestCase(19, 30)]
    [TestCase(30, 19)]
    [TestCase(20, 20)]
    public void InvalidExpansionNeverChangesDimensionsOrPlacements(int width, int height)
    {
        WarehouseGrid grid = new WarehouseGrid(20, 20);
        ItemInstance item = CreateItem(2, 3);
        Assert.That(grid.TryAdd(item, 17, 18, true), Is.EqualTo(WarehouseResult.Success));
        IReadOnlyList<WarehousePlacement> before = grid.GetPlacements();
        Assert.That(grid.CanExpand(width, height), Is.EqualTo(WarehouseResult.InvalidConfiguration));
        Assert.That(grid.TryExpand(width, height), Is.EqualTo(WarehouseResult.InvalidConfiguration));
        Assert.That(grid.Width, Is.EqualTo(20));
        Assert.That(grid.Height, Is.EqualTo(20));
        CollectionAssert.AreEqual(before, grid.GetPlacements());
    }

    [Test]
    public void SingleAxisExpansionKeepsExactPlacementAndAddsUsableCells()
    {
        WarehouseGrid grid = new WarehouseGrid(3, 5);
        ItemInstance item = CreateItem(2, 3);
        Assert.That(grid.TryAdd(item, 0, 3, true), Is.EqualTo(WarehouseResult.Success));
        IReadOnlyList<WarehousePlacement> before = grid.GetPlacements();
        Assert.That(grid.TryExpand(4, 5), Is.EqualTo(WarehouseResult.Success));
        CollectionAssert.AreEqual(before, grid.GetPlacements());
        Assert.That(grid.Width, Is.EqualTo(4));
        Assert.That(grid.Height, Is.EqualTo(5));
        Assert.That(grid.TryAdd(CreateItem(1, 5), 3, 0, false), Is.EqualTo(WarehouseResult.Success));
    }

    private ItemInstance CreateItem(int width, int height, bool canRotate = true)
    {
        ItemDataSO definition = ScriptableObject.CreateInstance<ItemDataSO>();
        createdObjects.Add(definition);
        definition.definitionId = "warehouse-grid-" + nextDefinition++;
        definition.nameZh = "测试物品";
        definition.nameEn = "Test item";
        definition.descriptionZh = "仓库格子测试物品";
        definition.descriptionEn = "Warehouse grid test item";
        definition.nameKey = definition.definitionId + ".name";
        definition.descriptionKey = definition.definitionId + ".description";
        definition.width = width;
        definition.height = height;
        definition.canRotate = canRotate;
        Assert.That(items.TryCreateInstance(definition, ItemOwner.Storage, "shelter-warehouse", "test", "",
            out ItemInstance item), Is.True);
        return item;
    }
}
