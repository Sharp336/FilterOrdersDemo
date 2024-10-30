using NLog;
using System.Text.Json;

namespace FilterOrdersDemo.Tests
{
    public class ProgramTests
    {
        [Theory]
        [InlineData("_cityDistrict=District_1", "_cityDistrict", "DefaultDistrict", "District_1")]
        [InlineData("_firstDeliveryDateTime=2024-10-30 09:00:00", "_cityDistrict", "DefaultDistrict", "DefaultDistrict")]
        public void GetArgumentValue_ShouldReturnCorrectValue(string arg, string argumentName, string defaultValue, string expected)
        {
            string[] args = { arg };

            string result = Program.GetArgumentValue(args, argumentName, defaultValue);

            Assert.Equal(expected, result);
        }

        [Fact]
        public void LoadOrders_ShouldThrowFileNotFoundException_WhenFileDoesNotExist()
        {
            string path = "non_existing_file.json";

            Assert.Throws<FileNotFoundException>(() => Order.LoadOrders(path));
        }

        [Fact]
        public void LoadOrders_ShouldReturnOrdersList_WhenFileExists()
        {
            string path = "test_orders.json";
            var orders = new List<Order>
            {
                new Order { OrderId = "Order_1", Weight = 5.0, District = "District_1", DeliveryTime = DateTime.Now }
            };
            File.WriteAllText(path, JsonSerializer.Serialize(orders, new JsonSerializerOptions { WriteIndented = true }));

            var result = Order.LoadOrders(path);

            Assert.NotNull(result);
            Assert.Single(result);
            Assert.Equal("Order_1", result.First().OrderId);

            File.Delete(path);
        }

        [Fact]
        public void GetValidOrders_ShouldReturnOnlyValidOrders()
        {
            var orders = new List<Order>
            {
                new Order { OrderId = "Order_1", Weight = 5.0, District = "District_1", DeliveryTime = DateTime.Now },
                new Order { OrderId = "", Weight = -1.0, District = "", DeliveryTime = default }
            };

            var result = Order.GetValidOrders(orders);

            Assert.Single(result);
            Assert.Equal("Order_1", result.First().OrderId);
        }

        [Theory]
        [InlineData("District_1", "2024-10-30T09:00:00", 2, new[] { "Order_1", "Order_2" })]
        [InlineData("District_2", "2024-10-30T09:00:00", 0, new string[] { })]
        public void FilterOrders_ShouldReturnCorrectOrdersWithinTimeWindow(string cityDistrict, string startTime, int expectedCount, string[] expectedOrderIds)
        {
            var orders = new List<Order>
            {
                new Order { OrderId = "Order_1", Weight = 5.0, District = "District_1", DeliveryTime = DateTime.Parse("2024-10-30T09:00:00") },
                new Order { OrderId = "Order_2", Weight = 5.0, District = "District_1", DeliveryTime = DateTime.Parse("2024-10-30T09:15:00") },
                new Order { OrderId = "Order_3", Weight = 5.0, District = "District_1", DeliveryTime = DateTime.Parse("2024-10-30T10:00:00") }
            };
            DateTime firstDeliveryDateTime = DateTime.Parse(startTime);

            var filteredOrders = Program.FilterOrders(orders, cityDistrict, firstDeliveryDateTime);

            Assert.Equal(expectedCount, filteredOrders.Count());
            foreach (var orderId in expectedOrderIds)
            {
                Assert.Contains(filteredOrders, o => o.OrderId == orderId);
            }
        }

        [Fact]
        public void RunApplication_ShouldLogWarning_WhenNoValidOrdersFound()
        {
            var args = new[] { "_cityDistrict=District_1", "_firstDeliveryDateTime=2024-10-30 09:00:00", "_deliveryLog=log.json", "_deliveryOrder=result.json" };
            var config = new Config
            {
                DefaultCityDistrict = "District_1",
                DefaultFirstDeliveryDateTime = "2024-10-30 09:00:00",
                DefaultDeliveryLogPath = "log.json",
                DefaultDeliveryOrderPath = "result.json",
                OrdersFilePath = "empty_orders.json"
            };
            File.WriteAllText(config.OrdersFilePath, JsonSerializer.Serialize(new List<Order> { }, new JsonSerializerOptions { WriteIndented = true }));

            Config.SaveNewConfig("config.json", config);

            Program.RunApplication(args);

            LogManager.Shutdown();

            string logContents = File.ReadAllText(config.DefaultDeliveryLogPath);
            Assert.Contains("Не найдены валидные заказы", logContents);

            File.Delete(config.OrdersFilePath);
            File.Delete(config.DefaultDeliveryLogPath);
        }

    }
}
