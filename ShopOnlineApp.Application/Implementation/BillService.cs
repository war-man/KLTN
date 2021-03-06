﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ShopOnlineApp.Application.Interfaces;
using ShopOnlineApp.Application.ViewModels.Bill;
using ShopOnlineApp.Application.ViewModels.Color;
using ShopOnlineApp.Application.ViewModels.Size;
using ShopOnlineApp.Data.EF.Common;
using ShopOnlineApp.Data.Enums;
using ShopOnlineApp.Data.IRepositories;
using ShopOnlineApp.Infrastructure.Interfaces;
using ShopOnlineApp.Utilities.Enum;

namespace ShopOnlineApp.Application.Implementation
{
    public class BillService : IBillService
    {
        #region Private Method
        private readonly IBillRepository _orderRepository;
        private readonly IBillDetailRepository _orderDetailRepository;
        private readonly IColorRepository _colorRepository;
        private readonly ISizeRepository _sizeRepository;
        private readonly IProductRepository _productRepository;
        private readonly IUnitOfWork _unitOfWork;
        #endregion
        #region Constructor
        public BillService(IBillRepository orderRepository,
            IBillDetailRepository orderDetailRepository,
            IColorRepository colorRepository,
            IProductRepository productRepository,
            ISizeRepository sizeRepository,
            IUnitOfWork unitOfWork)
        {
            _orderRepository = orderRepository;
            _orderDetailRepository = orderDetailRepository;
            _colorRepository = colorRepository;
            _sizeRepository = sizeRepository;
            _productRepository = productRepository;
            _unitOfWork = unitOfWork;
        }

        #endregion
        #region Public method
        public async Task<BillViewModel> Create(BillViewModel billVm)
        {
            try
            {
                //var order = Mapper.Map<BillViewModel, Bill>(billVm);

                var order = new BillViewModel().Map(billVm);

                // var orderDetails = Mapper.Map<List<BillDetailViewModel>, List<BillDetail>>(billVm.BillDetails);

                var orderDetails = new BillDetailViewModel().Map(billVm.BillDetails.AsParallel().AsOrdered().WithDegreeOfParallelism(3)).ToList();

                foreach (var detail in orderDetails)
                {
                    var product =await _productRepository.FindById(detail.ProductId);
                    detail.Price = product.Price;
                }
                order.BillDetails = orderDetails;
                var billEntity = await _orderRepository.AddAsync(order);

                return new BillViewModel().Map(billEntity);

            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }

        }

        public async Task Update(BillViewModel billVm)
        {
            var order = new BillViewModel().Map(billVm);
            var newDetails = order.BillDetails;
            var addedDetails = newDetails.Where(x => x.Id == 0).ToList();
            var updatedDetails = newDetails.Where(x => x.Id != 0).ToList();
            var existedDetails = (await _orderDetailRepository.FindAll(x => x.BillId == billVm.Id)).AsNoTracking();
            order.BillDetails.Clear();
            foreach (var detail in updatedDetails)
            {
                var product = await _productRepository.FindById(detail.ProductId);
                detail.Price = product.Price;
                await _orderDetailRepository.Update(detail);
            }

            foreach (var detail in addedDetails)
            {
                var product = await _productRepository.FindById(detail.ProductId);
                detail.Price = product.Price;
                await _orderDetailRepository.Add(detail);
            }
            await _orderDetailRepository.RemoveMultiple(existedDetails.Except(updatedDetails).ToList());
            order.DateCreated = DateTime.Now;
            order.DateModified = DateTime.Now;
            await _orderRepository.Update(order);
        }

        public async Task UpdateStatus(int billId, BillStatus status)
        {
            var order = await _orderRepository.FindById(billId);
            order.BillStatus = status;
            await _orderRepository.Update(order);
            _unitOfWork.Commit();
        }

        public async Task<List<SizeViewModel>> GetSizes()
        {
            return new SizeViewModel().Map((await _sizeRepository.FindAll()).AsNoTracking()).ToList();
        }

        public void Save()
        {
            _unitOfWork.Commit();
        }

        public async Task<IEnumerable<BillViewModel>> GetOrdersByCustomer(Guid customerId)
        {
            var data = (await _orderRepository.FindAll(x => x.CustomerId == customerId, c => c.BillDetails)).AsParallel().WithExecutionMode(ParallelExecutionMode.ForceParallelism);
            return new BillViewModel().Map(data);
        }

        public async Task<BillViewModel> GetDetail(int billId)
        {
            var bill = await _orderRepository.FindSingle(x => x.Id == billId, c => c.BillDetails);

            var billVm = new BillViewModel().Map(bill);
            billVm.BillDetails = await GetBillDetails(billId);
            return billVm;
        }

        public async Task<List<BillDetailViewModel>> GetBillDetails(int billId)
        {
            return new BillDetailViewModel().Map((await _orderDetailRepository
                    .FindAll(x => x.BillId == billId, c => c.Bill, c => c.Color, c => c.Size, c => c.Product)).AsNoTracking().AsParallel().AsOrdered().WithDegreeOfParallelism(3)).ToList();
        }

        public async Task<List<ColorViewModel>> GetColors()
        {
            return new ColorViewModel().Map((await _colorRepository.FindAll()).AsNoTracking()).ToList();
        }

        public async Task<BillDetailViewModel> CreateDetail(BillDetailViewModel billDetailVm)
        {
            var billDetail = new BillDetailViewModel().Map(billDetailVm);
            await _orderDetailRepository.Add(billDetail);
            return billDetailVm;
        }

        public async Task DeleteDetail(int productId, int billId, int colorId, int sizeId)
        {
            var detail = await _orderDetailRepository.FindSingle(x => x.ProductId == productId
            && x.BillId == billId && x.ColorId == colorId && x.SizeId == sizeId);
            await _orderDetailRepository.Remove(detail);
        }

        public async Task<ColorViewModel> GetColor(int id)
        {
            return new ColorViewModel().Map(await _colorRepository.FindById(id));
        }
        public async Task<SizeViewModel> GetSize(int id)
        {
            return new SizeViewModel().Map(await _sizeRepository.FindById(id));
        }

        public async Task<BaseReponse<ModelListResult<BillViewModel>>> GetAllPaging(BillRequest request)
        {
            var query = (await _orderRepository.FindAll()).AsNoTracking().AsParallel();
            if (!string.IsNullOrEmpty(request.StartDate))
            {
                DateTime start = DateTime.ParseExact(request.StartDate, "dd/MM/yyyy", CultureInfo.GetCultureInfo("vi-VN"));
                query = query.AsParallel().AsOrdered().WithDegreeOfParallelism(3).Where(x => x.DateCreated >= start);
            }
            if (!string.IsNullOrEmpty(request.EndDate))
            {
                DateTime end = DateTime.ParseExact(request.EndDate, "dd/MM/yyyy", CultureInfo.GetCultureInfo("vi-VN"));
                query = query.AsParallel().AsOrdered().WithDegreeOfParallelism(3).Where(x => x.DateCreated <= end);
            }
            if (!string.IsNullOrEmpty(request.SearchText))
            {
                query = query.AsParallel().AsOrdered().WithDegreeOfParallelism(3).Where(x => x.CustomerName.Contains(request.SearchText) || x.CustomerMobile.Contains(request.SearchText));
            }
            var totalRow = query.AsParallel().AsOrdered().WithDegreeOfParallelism(3).Count();

            var items = query.AsParallel().AsOrdered().WithDegreeOfParallelism(3)
                .OrderByDescending(x => x.DateCreated)
                .Skip(request.PageIndex * request.PageSize)
                .Take(request.PageSize);


            var result = new BaseReponse<ModelListResult<BillViewModel>>
            {
                Data = new ModelListResult<BillViewModel>
                {
                    Items = new BillViewModel().Map(items).ToList(),
                    Message = Message.Success,
                    RowCount = totalRow,
                    PageSize = request.PageSize,
                    PageIndex = request.PageIndex
                },
                Message = Message.Success,
                Status = (int)QueryStatus.Success
            };
            return result;
        }
        #endregion
    }
}
