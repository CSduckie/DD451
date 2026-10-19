using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class BackpackGridTests
{
    private readonly List<UnityEngine.Object> createdObjects = new List<UnityEngine.Object>();
    private ItemManager items;
    private int nextDefinition;

    [SetUp]
    public void SetUp()
    {
        GameObject root = new GameObject("Backpack Grid Test Items");
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
    public void DefaultConfigIsFourByFourAndInvalidGridSizesAreRejected()
    {
        BackpackConfigSO config = ScriptableObject.CreateInstance<BackpackConfigSO>();
        createdObjects.Add(config);
        Assert.That(config.width, Is.EqualTo(4));
        Assert.That(config.height, Is.EqualTo(4));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BackpackGrid(0, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BackpackGrid(4, -1));
    }

    [Test]
    public void NonSquareGridChecksWholeRotatedRectangleAndExtremeCoordinates()
    {
        BackpackGrid grid = new BackpackGrid(3, 5);
        ItemInstance item = CreateItem(2, 3);
        Assert.That(grid.TryAdd(item, 0, 0, false), Is.EqualTo(BackpackResult.Success));
        Assert.That(grid.TryMove(item.InstanceId, 0, 3, true), Is.EqualTo(BackpackResult.Success));
        Assert.That(grid.TryGetPlacement(item.InstanceId, out BackpackPlacement original), Is.True);
        Assert.That(original.Width, Is.EqualTo(3));
        Assert.That(original.Height, Is.EqualTo(2));

        Assert.That(grid.TryMove(item.InstanceId, 1, 3, true), Is.EqualTo(BackpackResult.OutOfBounds));
        Assert.That(grid.TryMove(item.InstanceId, 0, 4, true), Is.EqualTo(BackpackResult.OutOfBounds));
        Assert.That(grid.TryMove(item.InstanceId, int.MaxValue, 0, false), Is.EqualTo(BackpackResult.OutOfBounds));
        Assert.That(grid.TryMove(item.InstanceId, 0, int.MinValue, false), Is.EqualTo(BackpackResult.OutOfBounds));
        Assert.That(grid.TryGetPlacement(item.InstanceId, out BackpackPlacement after), Is.True);
        Assert.That(after, Is.SameAs(original));
        Assert.That(item.Definition.width, Is.EqualTo(2));
        Assert.That(item.Definition.height, Is.EqualTo(3));
    }

    [Test]
    public void MoveIgnoresOnlyItsOwnCellsAndFailedMoveKeepsBothPlacements()
    {
        BackpackGrid grid = new BackpackGrid(4, 4);
        ItemInstance moving = CreateItem(2, 1);
        ItemInstance blocker = CreateItem(1, 2);
        Assert.That(grid.TryAdd(moving, 0, 0, false), Is.EqualTo(BackpackResult.Success));
        Assert.That(grid.TryAdd(blocker, 3, 0, false), Is.EqualTo(BackpackResult.Success));
        IReadOnlyList<BackpackPlacement> before = grid.GetPlacements();

        Assert.That(grid.CanMove(moving.InstanceId, 1, 0, false), Is.EqualTo(BackpackResult.Success));
        Assert.That(grid.TryMove(moving.InstanceId, 2, 0, false), Is.EqualTo(BackpackResult.Overlap));
        Assert.That(grid.TryMove(moving.InstanceId, 3, 0, true), Is.EqualTo(BackpackResult.Overlap));
        CollectionAssert.AreEqual(before, grid.GetPlacements());

        Assert.That(grid.TryMove(moving.InstanceId, 1, 0, false), Is.EqualTo(BackpackResult.Success));
        Assert.That(grid.TryGetPlacement(moving.InstanceId, out BackpackPlacement moved), Is.True);
        Assert.That(moved.X, Is.EqualTo(1));
        Assert.That(moved.Item, Is.SameAs(moving));
        Assert.That(grid.GetPlacements()[1], Is.SameAs(before[1]));
        Assert.That(before[0].X, Is.Zero, "旧快照不能被后续移动改写。");
        Assert.That(moving.UsesThisRound, Is.Zero);
        Assert.That(moving.Owner, Is.EqualTo(ItemOwner.Backpack));
    }

    [Test]
    public void DuplicateIdentityAndForbiddenRotationDoNotChangeLayout()
    {
        BackpackGrid grid = new BackpackGrid(4, 4);
        ItemInstance item = CreateItem(1, 2, false);
        Assert.That(grid.TryAdd(item, 0, 0, false), Is.EqualTo(BackpackResult.Success));
        IReadOnlyList<BackpackPlacement> before = grid.GetPlacements();
        Assert.That(grid.TryAdd(item, 2, 0, false), Is.EqualTo(BackpackResult.DuplicateInstance));
        Assert.That(grid.TryMove(item.InstanceId, 0, 1, true), Is.EqualTo(BackpackResult.RotationNotAllowed));
        Assert.That(grid.CanPlace(CreateItem(1, 1, false), 2, 2, true), Is.EqualTo(BackpackResult.RotationNotAllowed));
        CollectionAssert.AreEqual(before, grid.GetPlacements());
    }

    [Test]
    public void FragmentedFreeAreaCannotFitTwoByTwoRectangle()
    {
        BackpackGrid grid = new BackpackGrid(4, 4);
        for (int y = 1; y < 4; y += 2)
        {
            for (int x = 1; x < 4; x += 2)
            {
                Assert.That(grid.TryAdd(CreateItem(1, 1), x, y, false), Is.EqualTo(BackpackResult.Success));
            }
        }
        ItemInstance square = CreateItem(2, 2);
        IReadOnlyList<BackpackPlacement> before = grid.GetPlacements();
        for (int y = 0; y < 3; y++)
        {
            for (int x = 0; x < 3; x++)
            {
                Assert.That(grid.TryAdd(square, x, y, false), Is.EqualTo(BackpackResult.Overlap));
            }
        }
        Assert.That(16 - grid.Count, Is.EqualTo(12));
        CollectionAssert.AreEqual(before, grid.GetPlacements());
    }

    [Test]
    public void PreviewQueriesCanBeAbandonedWithoutChangingLayoutOrItemState()
    {
        BackpackGrid grid = new BackpackGrid(4, 4);
        ItemInstance item = CreateItem(1, 2);
        Assert.That(grid.TryAdd(item, 0, 0, false), Is.EqualTo(BackpackResult.Success));
        IReadOnlyList<BackpackPlacement> before = grid.GetPlacements();
        string owner = item.ContainerId;

        Assert.That(grid.CanMove(item.InstanceId, 2, 3, true), Is.EqualTo(BackpackResult.Success));
        Assert.That(grid.CanMove(item.InstanceId, 4, 3, true), Is.EqualTo(BackpackResult.OutOfBounds));
        Assert.That(grid.CanMove(item.InstanceId, -1, 0, false), Is.EqualTo(BackpackResult.OutOfBounds));
        // Cancel在控制器中只丢弃预览；核心从未提前删除或修改这一件物品。
        CollectionAssert.AreEqual(before, grid.GetPlacements());
        Assert.That(item.ContainerId, Is.EqualTo(owner));
        Assert.That(item.UsesThisRound, Is.Zero);
        Assert.That(item.Owner, Is.EqualTo(ItemOwner.Backpack));
    }

    [Test]
    public void ReadOnlySnapshotsCannotMutateLayoutAndRemovalDoesNotRewriteOldSnapshot()
    {
        BackpackGrid grid = new BackpackGrid(4, 4);
        ItemInstance item = CreateItem(1, 1);
        grid.TryAdd(item, 0, 0, false);
        IReadOnlyList<BackpackPlacement> snapshot = grid.GetPlacements();
        IList<BackpackPlacement> writableCast = snapshot as IList<BackpackPlacement>;
        Assert.That(writableCast.IsReadOnly, Is.True);
        Assert.Throws<NotSupportedException>(() => writableCast.Clear());
        Assert.That(grid.TryRemove(item.InstanceId, out BackpackPlacement removed), Is.EqualTo(BackpackResult.Success));
        Assert.That(removed, Is.SameAs(snapshot[0]));
        Assert.That(snapshot.Count, Is.EqualTo(1));
        Assert.That(grid.Count, Is.Zero);
        Assert.That(grid.TryRemove(item.InstanceId, out _), Is.EqualTo(BackpackResult.MissingPlacement));
    }

    [Test]
    public void InvalidDefinitionDimensionsAreRejectedBeforeAdding()
    {
        BackpackGrid grid = new BackpackGrid(4, 4);
        ItemInstance item = CreateItem(1, 1);
        item.Definition.width = 0;
        Assert.That(grid.TryAdd(item, 0, 0, false), Is.EqualTo(BackpackResult.InvalidConfiguration));
        Assert.That(grid.Count, Is.Zero);
    }

    [Test]
    public void TwentyCellSampleCannotAllFitSixteenCellBackpack()
    {
        BackpackGrid grid = new BackpackGrid(4, 4);
        ItemInstance doll = CreateItem(2, 3);
        ItemInstance medkit = CreateItem(2, 2);
        ItemInstance foodA = CreateItem(2, 2);
        ItemInstance foodB = CreateItem(2, 2);
        ItemInstance battery = CreateItem(1, 2);
        Assert.That(grid.TryAdd(doll, 0, 0, false), Is.EqualTo(BackpackResult.Success));
        Assert.That(grid.TryAdd(medkit, 2, 0, false), Is.EqualTo(BackpackResult.Success));
        Assert.That(grid.TryAdd(foodA, 2, 2, false), Is.EqualTo(BackpackResult.Success));
        Assert.That(grid.TryAdd(battery, 0, 3, true), Is.EqualTo(BackpackResult.Success));
        for (int y = 0; y < 3; y++)
        {
            for (int x = 0; x < 3; x++)
            {
                Assert.That(grid.CanPlace(foodB, x, y, false), Is.EqualTo(BackpackResult.Overlap));
            }
        }
        Assert.That(items.InstanceCount, Is.EqualTo(5));
        Assert.That(grid.Count, Is.EqualTo(4));
    }

    private ItemInstance CreateItem(int width, int height, bool canRotate = true)
    {
        ItemDataSO definition = ScriptableObject.CreateInstance<ItemDataSO>();
        createdObjects.Add(definition);
        definition.definitionId = "grid-test-" + nextDefinition++;
        definition.nameZh = "测试";
        definition.nameEn = "Test";
        definition.descriptionZh = "测试物品";
        definition.descriptionEn = "Test item";
        definition.nameKey = definition.definitionId + ".name";
        definition.descriptionKey = definition.definitionId + ".description";
        definition.width = width;
        definition.height = height;
        definition.canRotate = canRotate;
        Assert.That(items.TryCreateInstance(definition, ItemOwner.Backpack, "player-backpack", "test", "",
            out ItemInstance item), Is.True);
        return item;
    }
}
