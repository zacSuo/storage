﻿using System;
using System.Collections.Generic;
using System.Linq;
using Models;

namespace BLL
{
    /// <summary>
    /// 选择策略相关模块
    /// </summary>
    public class Choice
    {
        #region 选择订单
        /// <summary>
        /// 为拣货员选择订单
        /// </summary>
        /// <param name="staffID">拣货员ID</param>
        /// <returns>新订单ID列表</returns>
        public List<int> GetOrders4Picker(int staffID, int orderCount)
        {
            int recordCount = 0, updateIdx;
            List<int> orderIds = new List<int>();
            List<Models.Orders> orderList = DbEntity.DOrders.GetEntityList(" Status = 0 ", null, 1, orderCount, out recordCount);
            object realID;
            foreach (Models.Orders order in orderList)
            {
                order.PickTime = DateTime.Now;
                order.Picker = staffID;
                order.Status = 1;
                updateIdx = DbEntity.DOrders.Update(order);
                if (updateIdx > 0)
                {
                    realID = DbEntity.DRealOrders.Insert(new RealOrders()
                    {
                        OrderID = order.ID,
                        StaffID = staffID,
                        SkuList = order.SkuList,
                        ProductCount = order.productCount,
                        Status = 1
                    });
                    orderIds.Add(Convert.ToInt32(order.ID));
                }
            }

            return orderIds;
        }
        #endregion 

        #region 选择货架
        /// <summary>
        /// 根据订单列表选择货架
        /// </summary>
        /// <param name="staffPosition"></param>
        /// <param name="orderIds"></param>
        public void GetShelves(Location staffPosition, List<int> orderIds)
        {
            if (orderIds == null || orderIds.Count == 0)
            {
                string strMsg = "没有新订单";
                return;
            }
            List<SkuInfo> skuList = GetProductsByOrderID(orderIds);
            GetShelves(staffPosition, skuList);
        }

        /// <summary>
        /// 根据商品列表选择货架
        /// </summary>
        /// <param name="staffPosition"></param>
        /// <param name="skuList"></param>
        public void GetShelves(Location staffPosition, List<SkuInfo> skuList)
        {
            List<List<int>> shelfCollector = GetShelfBySkuID(skuList);
            if (shelfCollector == null || shelfCollector.Count == 0)
            {
                throw new Exception("库存不足");
            }
            List<int> shelfIds = GetAtomicItems(shelfCollector);
            List<Shelf> shelfInfo = GetShelvesInfo(shelfIds);

            int idx = GetMinDistanceShelf(shelfCollector, shelfInfo, staffPosition);

            foreach (int i in shelfCollector[idx])
            {
                foreach (Shelf shelf in shelfInfo)
                {
                    if (shelf.ID == i)
                    {
                        GlobalVariable.ShelvesNeedToMove.Add(new ShelfTarget(staffPosition, Core.StoreInfo.GetLocationByPointID(shelf.LocationID), shelf));
                        break;
                    }
                }
            }

        }

        #region 私有子函数 - 找可用货架

        /// <summary>
        /// 通过订单ID获取Sku列表
        /// </summary>
        /// <param name="orderId"></param>
        /// <returns></returns>
        private List<SkuInfo> GetProductsByOrderID(List<int> orderId)
        {
            string strWhere = string.Format(" ID IN ({0}) ", string.Join(",", orderId.ToArray()));
            List<RealOrders> orderList = DbEntity.DRealOrders.GetEntityList(strWhere, null);
            if (orderList == null) return null;

            List<SkuInfo> skuList = new List<SkuInfo>();
            foreach (RealOrders order in orderList)
            {
                string[] strSkus = order.SkuList.Split(';');
                foreach (string sku in strSkus)
                {
                    string[] strID = sku.Split(',');

                    skuList.Add(new SkuInfo()
                    {
                        ID = int.Parse(strID[0]),
                        Count = int.Parse(strID[1])
                    });
                }
            }
            return skuList;
        }

        /// <summary>
        /// 通过Sku ID 获取货架列表
        /// </summary>
        /// <param name="skuList">Sku 信息</param>
        /// <returns>满足条件的货架ID集合</returns>
        private List<List<int>> GetShelfBySkuID(List<SkuInfo> skuList)
        {
            if (skuList == null || skuList.Count == 0) return null;

            string strSkuId = string.Empty;
            foreach (SkuInfo sku in skuList)
            {
                strSkuId += sku.ID + ",";
            }
            string strWhere = string.Format(" SkuID IN ({0}) ", strSkuId.Remove(strSkuId.Length - 1));
            List<Models.Products> productList = DbEntity.DProducts.GetEntityList(strWhere, null);
            if (productList == null || productList.Count == 0) return null;
            //统计每个货架 对应的商品及数量
            Dictionary<int, Dictionary<int, int>> skuShelf = CountProductByShelf(productList);
            //计算所有满足商品数量的货架组合
            return GetShelvesCombination(skuList, skuShelf);
        }

        /// <summary>
        /// 统计每个货架 对应的商品及数量
        /// </summary>
        /// <param name="productList"></param>
        /// <returns>#int, Dictionary#int, int## = #货架ID, #商品ID, 货架商品数##</returns>
        private Dictionary<int, Dictionary<int, int>> CountProductByShelf(List<Models.Products> productList)
        {
            Dictionary<int, Dictionary<int, int>> productShelf = new Dictionary<int, Dictionary<int, int>>();
            foreach (Models.Products product in productList)
            {
                if (!productShelf.ContainsKey(product.ShelfID))
                {//新商品
                    Dictionary<int, int> shelf = new Dictionary<int, int>();
                    shelf.Add(product.SkuID, product.Count);
                    productShelf.Add(product.ShelfID, shelf);
                }
                else if (!productShelf[product.ShelfID].ContainsKey(product.SkuID))
                {//新货架
                    productShelf[product.ShelfID].Add(product.SkuID, product.Count);
                }
                else
                {//已有货架
                    productShelf[product.ShelfID][product.SkuID] += product.Count;
                }
            }
            return productShelf;
        }

        /// <summary>
        /// 计算所有商品满足数量的货架组合
        /// </summary>
        /// <param name="skuList">商品需求信息</param>
        /// <param name="skuShelf">货架对商品的供应（#int, Dictionary#int, int## = #货架ID, #商品ID, 货架商品数##）</param>
        /// <returns>满足条件的货架组合</returns>
        private List<List<int>> GetShelvesCombination(List<SkuInfo> skuList, Dictionary<int, Dictionary<int, int>> skuShelf)
        {
            int num = (int)Math.Pow(2, skuShelf.Count);
            List<int> position = null;
            List<List<int>> result = new List<List<int>>();
            for (int i = 1; i < num; i++)
            {
                Dictionary<int, int> productCount = new Dictionary<int, int>();
                position = GetOnePosition(i);
                //汇总货架中所有商品数量
                GatherProductByShelf(productCount, skuShelf, position);

                if (CheckShelfProductNum(skuList, productCount))
                {//判断货架中商品数量是否满足数量
                    result.Add(position);
                }
            }
            return GetAtomicCollection(result);
        }

        /// <summary>
        /// 获取一个数中所有1的位置
        /// N位数的所有组合，视为1在二进制中的位置
        /// </summary>
        /// <param name="num"></param>
        /// <returns></returns>
        private List<int> GetOnePosition(int num)
        {
            List<int> result = new List<int>();
            int tmp = num;
            for (int i = 1; tmp > 0; i++)
            {
                if ((tmp & 1) > 0)
                    result.Add(i);
                tmp >>= 1;
            }

            return result;
        }

        /// <summary>
        /// 汇总货架中所有商品数量
        /// </summary>
        /// <param name="productCount">汇总结果（#int,int# = #Sku ID, 数量#）</param>
        /// <param name="skuShelf">当前所有货架商品信息（#int, Dictionary#int, int## = #货架ID, #商品ID, 货架商品数##）</param>
        /// <param name="position">待统计的货架位置</param>
        private void GatherProductByShelf(Dictionary<int, int> productCount, Dictionary<int, Dictionary<int, int>> skuShelf, List<int> position)
        {
            foreach (int idx in position)
            {//统计组合中所有货架的商品数量
                KeyValuePair<int, Dictionary<int, int>> shelf = skuShelf.ElementAt(idx - 1);
                foreach (KeyValuePair<int, int> product in shelf.Value)
                {
                    if (productCount.ContainsKey(product.Key))
                        productCount[product.Key] += product.Value;
                    else
                        productCount.Add(product.Key, product.Value);
                }
            }
        }

        /// <summary>
        /// 判断货架中商品数量是否满足数量
        /// </summary>
        /// <param name="skuInfoList">商品需求信息</param>
        /// <param name="productCount">货架商品汇总</param>
        /// <returns></returns>
        private bool CheckShelfProductNum(List<SkuInfo> skuInfoList, Dictionary<int, int> productCount)
        {
            bool tmpFlag = true;
            foreach (SkuInfo sku in skuInfoList)
            {//判断是否全部商品满足数量
                if (!productCount.ContainsKey(sku.ID) || productCount[sku.ID] < sku.Count)
                {
                    tmpFlag = false;
                    break;
                }
            }

            return tmpFlag;
        }

        /// <summary>
        /// 获取集合的原子集
        /// </summary>
        /// <param name="shelfList"></param>
        /// <returns></returns>
        private List<List<int>> GetAtomicCollection(List<List<int>> itemList)
        {//长的不会是短的子集，所以，仅判断短的是否为长的子集
            if (itemList == null || itemList.Count == 0) return null;

            itemList.Sort((List<int> a, List<int> b) => b.Count - a.Count);
            foreach (List<int> item in itemList)
            {
                item.Sort((int a, int b) => b - a);
            }

            List<List<int>> result = new List<List<int>>();
            for (int i = 0; i < itemList.Count; i++)
            {//父集从长向短（从左向右）
                int j = itemList.Count - 1;
                for (; j > i; j--)
                {//子集从短向长（从右向左）
                    if (CheckListFather(itemList[i], itemList[j]))
                    {
                        i++;
                        break;
                    }
                }
                if (i == j)
                {//如果长集合不是任何短集合的父集，则为原子集
                    result.Add(itemList[i]);
                }
            }
            return result;
        }

        /// <summary>
        /// 检验是否一个集合包含另一集合
        /// </summary>
        /// <param name="father"></param>
        /// <param name="child"></param>
        /// <returns></returns>
        private bool CheckListFather(List<int> father, List<int> child)
        {//集合已经是有序的
            bool tmpFlag = true;
            for (int i = 0; i < child.Count; i++)
            {
                if (father[i] != child[i])
                {
                    tmpFlag = false;
                    break;
                }
            }
            return tmpFlag;
        }

        /// <summary>
        /// 获取集合中的所有元素
        /// </summary>
        /// <param name="itemList"></param>
        /// <returns></returns>
        private List<int> GetAtomicItems(List<List<int>> itemList)
        {
            if (itemList == null || itemList.Count == 0) return null;

            List<int> result = new List<int>();
            foreach (List<int> items in itemList)
            {
                foreach (int item in items)
                {
                    if (!result.Contains(item))
                    {
                        result.Add(item);
                    }
                }
            }
            return result;
        }
        #endregion

        #region 私有子函数 - 确定货架
        /// <summary>
        /// 获取货架信息
        /// </summary>
        /// <param name="shelfIds">货架ID</param>
        /// <returns></returns>
        private List<Shelf> GetShelvesInfo(List<int> shelfIds)
        {
            if (shelfIds == null || shelfIds.Count == 0) return null;

            string strWhere = string.Format(" ID IN ({0}) ", string.Join(",", shelfIds.ToArray()));
            List<Shelf> shelfOrigin = DbEntity.DShelf.GetEntityList(strWhere, null);

            return shelfOrigin;
        }

        /// <summary>
        /// 获取最小距离的货架集
        /// </summary>
        /// <param name="shelfIds">备选货架集合</param>
        /// <param name="shelfList">货架信息</param>
        /// <param name="target">拣货员位置（目标坐标）</param>
        /// <returns>选择的货架集合索引</returns>
        private int GetMinDistanceShelf(List<List<int>> shelfCollect, List<Shelf> shelfList, Location target)
        {
            //计算每个货架的距离
            Dictionary<int, int> shelfDistance = new Dictionary<int, int>();
            foreach (Shelf shelf in shelfList)
            {
                shelfDistance.Add(shelf.ID, Location.Manhattan(target, Core.StoreInfo.GetLocationByPointID(shelf.LocationID)));
            }
            //计算每个货架集合的总距离
            int[] shelfCollectDistance = new int[shelfCollect.Count];
            for (int i = 0; i < shelfCollect.Count; i++)
            {
                List<int> shelves = shelfCollect[i];
                foreach (int shelf in shelves)
                {
                    shelfCollectDistance[i] += shelfDistance[shelf];
                }
            }
            //计算距离最小的集合，相同距离选择货架最少的，都相同随机（索引靠前的集合）
            int idx = 0;
            for (int i = 1; i < shelfCollectDistance.Length; i++)
            {
                if (shelfCollectDistance[i] < shelfCollectDistance[idx] || shelfCollectDistance[i] == shelfCollectDistance[idx] && shelfCollect[i].Count < shelfCollect[idx].Count)
                {
                    idx = i;
                }
            }
            return idx;
        }

        #endregion
        #endregion

        #region 选择小车设备
        /// <summary>
        /// 获取所有空闲小车设备
        /// </summary>
        /// <returns></returns>
        public static List<RealDevice> GetAllStandbyDevices()
        {
            List<RealDevice> result = new List<RealDevice>();
            foreach (RealDevice device in GlobalVariable.RealDevices)
            {
                if (device.Status == (short)RealDeviceStatus.Standby)
                {
                    result.Add(device);
                }
            }

            return result;
        }

        /// <summary>
        /// 根据设备返回最近货架
        /// </summary>
        /// <param name="device"></param>
        /// <returns></returns>
        public static ErrorCode FindClosestShelf(RealDevice device, ref ShelfTarget shelf)
        {
            List<ShelfTarget> shelves = GlobalVariable.ShelvesNeedToMove;
            if (shelves.Count == 0) return  ErrorCode.CannotFindUseable;

            Location deviceLocation = Core.StoreInfo.GetLocationByPointID(device.LocationID);
            int idx = 0, minDistance = Location.Manhattan(deviceLocation, shelves[idx].Source);
            for (int i = 1; i < shelves.Count; i++)
            {
                if (minDistance > Location.Manhattan(deviceLocation, shelves[i].Source))
                {
                    idx = i;
                }
            }
            shelf = GlobalVariable.ShelvesNeedToMove[idx];
            GlobalVariable.ShelvesNeedToMove.RemoveAt(idx);

            return ErrorCode.OK;
        }

        #endregion

        #region 选择充电桩

        /// <summary>
        /// 找最近设备的充电桩
        /// </summary>
        /// <param name="deviceID"></param>
        /// <param name="station">目标充电桩</param>
        /// <returns></returns>
        public static ErrorCode FindClosestCharger(int deviceID,ref Station station)
        {
            List<Station> stationList = GlobalVariable.RealStation.FindAll(item => item.Status == (short)StoreComponentStatus.OK && item.Type == (short)StoreComponentType.Charger);
            if (stationList == null || stationList.Count == 0) return ErrorCode.CannotFindUseable;

            RealDevice device = GlobalVariable.RealDevices.Find(item => item.ID == deviceID);
            if (device == null) return ErrorCode.CannotFindByID;

            Location deviceLocation = Core.StoreInfo.GetLocationByPointID(device.LocationID);

            int minDistance = int.MaxValue, tmpDistance = 0;
            foreach (Station item in stationList)
            {
                tmpDistance = Location.Manhattan(Location.DecodeStringInfo(item.Location), deviceLocation);
                if (tmpDistance < minDistance)
                {
                    station = item;
                    minDistance = tmpDistance;
                }
            }
            return ErrorCode.OK;
        }
        #endregion
    }
}
