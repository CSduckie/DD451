using System;
using UnityEngine;

// 只在专用场景明确模拟已撤离样本；生产仓库从不把入仓等同于保有。
public class WarehouseTestDriver : MonoBehaviour
{
    public WarehouseUIController warehouseUI;
    public ShelterView shelterView;
    public ItemLibrarySO itemLibrary;
    private WarehouseManager warehouse;

    private void Start()
    {
        warehouse = WarehouseManager.Resolve();
        ShelterManager shelter = ShelterManager.Resolve();
        LocalizationManager language = LocalizationManager.Resolve();
        if (warehouse == null || shelter == null || language == null)
        {
            Debug.LogError("WarehouseTest缺少已接线的真实常驻模块。", this);
            return;
        }
        warehouse.SetShelterContext(true, gameObject.scene);
        if (warehouse.Items.InstanceCount == 0 && warehouse.GetPlacements().Count == 0)
        {
            CreateSample("doll", ItemOwner.Storage, true, 0, 0);
            CreateSample("medkit", ItemOwner.Backpack, true, 0, 0);
            CreateSample("food", ItemOwner.Storage, false, 3, 0);
            CreateSample("battery", ItemOwner.Storage, false, 6, 0);
            warehouse.NotifyChanged();
        }
        warehouseUI.Bind(warehouse, language);
        shelterView.Bind(shelter, language);
        shelterView.warehouseUI = warehouseUI;
        warehouseUI.Open();
        shelter.RefreshEv();
    }

    private void CreateSample(string definitionId, ItemOwner owner, bool retained, int x, int y)
    {
        ItemDataSO definition = itemLibrary.FindDefinition(definitionId);
        string container = owner == ItemOwner.Storage ? warehouse.ContainerId : "player-backpack";
        if (!warehouse.Items.TryCreateInstance(definition, owner, container, "warehouse-test",
            "warehouse-test/origin/" + definitionId, out ItemInstance item))
        {
            throw new InvalidOperationException("WarehouseTest无法创建样本：" + definitionId);
        }
        if (owner == ItemOwner.Storage && warehouse.TryAddPlacement(item.InstanceId, x, y, false) != WarehouseResult.Success)
        {
            throw new InvalidOperationException("WarehouseTest样本摆放失败：" + definitionId);
        }
        if (retained) { warehouse.Items.TrySetRetained(item.InstanceId, true); }
    }

    private void OnDisable()
    {
        if (warehouseUI != null) { warehouseUI.Close(); }
        if (warehouse != null) { warehouse.SetShelterContext(false, gameObject.scene); }
    }
}
