﻿using System;
using NUnit.Framework;
using Moq;
using Suteki.Shop.Controllers;
using Suteki.Shop.ViewData;
using System.Collections.Generic;
using Suteki.Shop.Tests.Repositories;
using Suteki.Shop.Repositories;
using System.Collections.Specialized;
using System.Threading;
using System.Security.Principal;
using Suteki.Shop.Services;
using System.Web;
using System.Web.Mvc;

namespace Suteki.Shop.Tests.Controllers
{
    [TestFixture]
    public class ProductControllerTests
    {
        ProductController productController;
        Mock<ProductController> productControllerMock;
        ControllerTestContext testContext;

        Repository<Product> productRepository;
        Mock<Repository<Product>> productRepositoryMock;

        Repository<Category> categoryRepository;
        Mock<Repository<Category>> categoryRepositoryMock;

        IHttpFileService httpFileService;
        ISizeService sizeService;
        IOrderableService<Product> productOrderableService;

        [SetUp]
        public void SetUp()
        {
            // you have to be an administrator to access the product controller
            Thread.CurrentPrincipal = new GenericPrincipal(new GenericIdentity("admin"), new string[] { "Administrator" });

            categoryRepositoryMock = MockRepositoryBuilder.CreateCategoryRepository();
            categoryRepository = categoryRepositoryMock.Object;

            productRepositoryMock = MockRepositoryBuilder.CreateProductRepository();
            productRepository = productRepositoryMock.Object;

            httpFileService = new Mock<IHttpFileService>().Object;
            sizeService = new Mock<ISizeService>().Object;

            productOrderableService = new Mock<IOrderableService<Product>>().Object;

            productControllerMock = new Mock<ProductController>(
                productRepository, 
                categoryRepository,
                httpFileService,
                sizeService,
                productOrderableService);

            productController = productControllerMock.Object;
            testContext = new ControllerTestContext(productController);
        }

        [Test]
        public void Index_ShouldShowProductListForCategoryOnIndexView()
        {
            int categoryId = 4;

            Category category = new Category
            {
                CategoryId = categoryId,
                Products =
                {
                    new Product(),
                    new Product()
                }
            };

            categoryRepositoryMock.Expect(r => r.GetById(categoryId)).Returns(category).Verifiable();

            RenderViewResult result = productController.Index(categoryId) as RenderViewResult;

            Assert.AreEqual("Index", result.ViewName);
            ShopViewData viewData = result.ViewData as ShopViewData;
            Assert.IsNotNull(viewData, "viewData is not ShopViewData");
            Assert.IsNotNull(viewData.Products, "viewData.Products should not be null");
            Assert.IsNotNull(viewData.Category, "viewData.Category should not be null");
            Assert.AreEqual(categoryId, viewData.Category.CategoryId);

            ProductRepositoryExtensionsTests.AssertProductsReturnedBy_WhereCategoryIdIs4_AreCorrect(viewData.Products);
            categoryRepositoryMock.Verify();
        }

        [Test]
        public void Item_ShouldShowItemView()
        {
            int productId = 3;
            Product product = new Product();

            productRepositoryMock.Expect(r => r.GetById(productId)).Returns(product);

            RenderViewResult result = productController.Item(productId) as RenderViewResult;

            Assert.AreEqual("Item", result.ViewName);
            ShopViewData viewData = result.ViewData as ShopViewData;
            Assert.IsNotNull(viewData, "viewData is not ShopViewData");
            Assert.IsNotNull(viewData.Product, "viewData.Product should not be null");
            Assert.AreSame(product, viewData.Product, "viewData.Product is not the same as the test product");
        }

        [Test]
        public void New_ShouldShowDefaultProductInEditView()
        {
            int categoryId = 4;

            RenderViewResult result = productController.New(categoryId) as RenderViewResult;

            AssertEditViewIsCorrectlyCalled(result);
        }

        private void AssertEditViewIsCorrectlyCalled(RenderViewResult result)
        {
            Assert.AreEqual("Edit", result.ViewName);
            ShopViewData viewData = result.ViewData as ShopViewData;
            Assert.IsNotNull(viewData, "viewData is not ShopViewData");
            Assert.IsNotNull(viewData.Product, "viewData.Product should not be null");
            Assert.IsNotNull(viewData.Categories, "viewData.Categories should not be null");
        }

        [Test]
        public void Edit_ShouldShowProductInEditView()
        {
            int productId = 44;

            Product product = new Product();
            productRepositoryMock.Expect(r => r.GetById(productId)).Returns(product).Verifiable();

            RenderViewResult result = productController.Edit(productId) as RenderViewResult;

            AssertEditViewIsCorrectlyCalled(result);
            productRepositoryMock.Verify();
        }

        [Test]
        public void Update_ShouldInsertTheNewProductIntoRepository()
        {
            int productId = 0; // means it's a new product
            int categoryId = 4;
            string name = "My New Product";
            string description = "A description of my new product";

            // create the form
            NameValueCollection form = new NameValueCollection();
            form.Add("productid", productId.ToString());
            form.Add("categoryid", categoryId.ToString());
            form.Add("name", name);
            form.Add("description", description);
            testContext.TestContext.RequestMock.ExpectGet(r => r.Form).Returns(() => form);

            // add expectations for product repository insertion
            Product product = null;

            productRepositoryMock.Expect(r => r.InsertOnSubmit(It.IsAny<Product>()))
                .Callback<Product>(p => { product = p; })
                .Verifiable();
            productRepositoryMock.Expect(r => r.SubmitChanges()).Verifiable();

            // add expectations for image upload
            List<Image> images = new List<Image>
            {
                new Image(),
                new Image()
            };

            HttpRequestBase httpRequest = testContext.TestContext.Request;
            Mock.Get(httpFileService).Expect(h => h.GetUploadedImages(httpRequest)).Returns(images).Verifiable();

            // expect the size service to be called
            Mock.Get(sizeService).Expect(s => s.WithVaues(form)).Returns(sizeService).Verifiable();
            Mock.Get(sizeService).Expect(s => s.Update(It.IsAny<Product>())).Verifiable();

            // excercise the method
            RenderViewResult result = productController.Update(productId) as RenderViewResult;

            AssertEditViewIsCorrectlyCalled(result);

            // assert the product was created correctly
            Assert.AreEqual(categoryId, product.CategoryId, "product.CategoryId is incorrect");
            Assert.AreEqual(name, product.Name, "product.Name is incorrect");
            Assert.AreEqual(description, product.Description, "product.Description is incorrect");

            // assert the images were added to the product
            Assert.AreSame(images[0], product.ProductImages[0].Image, "First product image was not added");
            Assert.AreSame(images[1], product.ProductImages[1].Image, "Second product image was not added");

            productRepositoryMock.Verify();
            Mock.Get(httpFileService).Verify();
            Mock.Get(sizeService).Verify();
        }

        [Test]
        public void Update_ShouldUpdateAnExistingProduct()
        {
            int productId = 44; // non-zero means it's an existing product
            int categoryId = 4;
            string name = "My New Product";
            string description = "A description of my new product";

            // create the form
            NameValueCollection form = new NameValueCollection();
            form.Add("productid", productId.ToString());
            form.Add("categoryid", categoryId.ToString());
            form.Add("name", name);
            form.Add("description", description);
            testContext.TestContext.RequestMock.ExpectGet(r => r.Form).Returns(() => form);

            Product product = new Product
            {
                ProductId = productId,
                CategoryId = 6,
                Name = "The old name",
                Description = "The old description"
            };

            productRepositoryMock.Expect(r => r.GetById(productId)).Returns(product).Verifiable();
            productRepositoryMock.Expect(r => r.SubmitChanges()).Verifiable();

            // expect the size service to be called
            Mock.Get(sizeService).Expect(s => s.WithVaues(form)).Returns(sizeService).Verifiable();
            Mock.Get(sizeService).Expect(s => s.Update(It.IsAny<Product>())).Verifiable();

            // excercise the method
            RenderViewResult result = productController.Update(productId) as RenderViewResult;

            AssertEditViewIsCorrectlyCalled(result);

            // assert the product was created correctly
            Assert.AreEqual(categoryId, product.CategoryId, "product.CategoryId is incorrect");
            Assert.AreEqual(name, product.Name, "product.Name is incorrect");
            Assert.AreEqual(description, product.Description, "product.Description is incorrect");

            productRepositoryMock.Verify();
        }
    }
}