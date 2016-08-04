﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BLL
{
    /// <summary>
    /// 订单相关逻辑处理
    /// </summary>
    public class Orders
    {
        /// <summary>
        /// 获取实时订单信息
        /// </summary>
        /// <param name="orderIds">订单ID</param>
        /// <returns></returns>
        public List<Models.RealOrders> GetRealOrderList(List<int> orderIds)
        {
            if (orderIds == null || orderIds.Count == 0) return null;

            string strOrderIds = string.Join(",", orderIds.ToArray<int>());
            string strWhere = string.Format(" OrderID in ({0}) ", strOrderIds);

            return Models.DbEntity.DRealOrders.GetEntityList(strWhere, null);
        }

        /// <summary>
        /// 获取一个实时订单
        /// </summary>
        /// <param name="orderId"></param>
        /// <returns></returns>
        public Models.RealOrders GetRealOrder(int orderId)
        {
            string strWhere = string.Format(" OrderID = {0} ", orderId);

            return Models.DbEntity.DRealOrders.GetSingleEntity(strWhere, null);
        }

        /// <summary>
        /// 更新实时订单数据
        /// </summary>
        /// <param name="realOrder"></param>
        /// <returns></returns>
        public Models.ErrorCode UpdateRealOrder(Models.RealOrders realOrder)
        {
            Models.ErrorCode code = Models.ErrorCode.CannotFindUseable;

            if (realOrder != null)
                code =  Models.DbEntity.DRealOrders.Update(realOrder)>0?Models.ErrorCode.OK: Models.ErrorCode.DatabaseHandler;

            return code;
        }
    }
}