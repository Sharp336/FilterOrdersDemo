using System.Text.Json;
using NLog;
using NLog.Config;
using NLog.Targets;

namespace FilterOrdersDemo
{
    class Program
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        private static readonly string configPath = "config.json";

        static void Main(string[] args)
        {
            try
            {
                Config config = Config.Load(configPath);

                string deliveryLogPath = GetArgumentValue(args, "_deliveryLog", config.DefaultDeliveryLogPath);

                ConfigureLogging(deliveryLogPath);

                logger.Info($"Запуск {DateTime.Now}, " + (args.Any() ? $"аргументы: {string.Join(",\n", args)}" : "аргументов не передано"));

                string cityDistrict = GetArgumentValue(args, "_cityDistrict", config.DefaultCityDistrict);
                string firstDeliveryDateTimeStr = GetArgumentValue(args, "_firstDeliveryDateTime", config.DefaultFirstDeliveryDateTime);
                string deliveryOrderPath = GetArgumentValue(args, "_deliveryOrder", config.DefaultDeliveryOrderPath);

                DateTime firstDeliveryDateTime;
                if (!DateTime.TryParse(firstDeliveryDateTimeStr, out firstDeliveryDateTime))
                {
                    logger.Warn("Некорректный формат времени первой доставки.");
                    return;
                }

                // Чтение данных из файла с заказами с валидацией
                List<Order> orders = Order.LoadOrders(config.OrdersFilePath);
                orders = Order.ValidateOrders(orders);
                if (orders.Count == 0)
                {
                    logger.Warn($"Не найдены валидные заказы в файле: {config.OrdersFilePath}");
                    return;
                }

                // Фильтрация заказов по указанным параметрам
                var filteredOrders = orders.Where(order => order.District == cityDistrict &&
                                                           order.DeliveryTime >= firstDeliveryDateTime &&
                                                           order.DeliveryTime <= firstDeliveryDateTime.AddMinutes(30))
                                                           .OrderBy(ord => ord.DeliveryTime)
                                                           .ToList();

                // Запись результата в файл
                Order.SaveOrders(filteredOrders, deliveryOrderPath);

                // Логирование операций
                logger.Info($"Найдено {filteredOrders.Count} валидных заказов для района {cityDistrict} из файла: {config.OrdersFilePath}.");
            }
            catch (ArgumentException ex)
            {
                logger.Warn(ex, "Ошибка в аргументах командной строки:");
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "Ошибка выполнения:");
            }
            finally
            {
                LogManager.Shutdown();
            }
        }

        static void ConfigureLogging(string logPath)
        {
            var config = new LoggingConfiguration();

            var logfile = new FileTarget("logfile")
            {
                FileName = logPath,
                Layout = "${longdate} ${uppercase:${level}} ${message} ${onexception:inner=${newline}${exception:format=Data,tostring:innerExceptionSeparator=\n:separator=\n:exceptionDataSeparator=\n}}",
            };

            config.AddRule(LogLevel.Info, LogLevel.Fatal, logfile);

            LogManager.Configuration = config;
        }


        static string GetArgumentValue(string[] args, string argumentName, string defaultValue)
        {
            try
            {
                string? value = args.FirstOrDefault(arg => arg.StartsWith(argumentName + "="))?.Split('=')[1];
                return string.IsNullOrEmpty(value) ? defaultValue : value;
            }
            catch (Exception ex)
            {
                ex.Data.Add("Comment", $"Ошибка чтения аргумента {argumentName}");
                throw;
            }
        }
    }

    public class Config
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        public string DefaultCityDistrict { get; set; }
        public string DefaultFirstDeliveryDateTime { get; set; }
        public string DefaultDeliveryLogPath { get; set; }
        public string DefaultDeliveryOrderPath { get; set; }
        public string OrdersFilePath { get; set; }

        public static Config Load(string path)
        {
            if (!File.Exists(path)) return CreateNewConfig(path);

            try
            {
                var config = JsonSerializer.Deserialize<Config>(File.ReadAllText(path));
                if (config == null)
                {
                    logger.Warn("Загруженный конфиг пуст, создан новый");
                    return CreateNewConfig(path);
                }
                return config;
            }
            catch (JsonException ex)
            {
                ex.Data.Add("Comment", "Ошибка десериализации конфига");
                throw;
            }
        }

        public static Config CreateNewConfig(string path)
        {
            Config defaultConfig = new Config
            {
                DefaultCityDistrict = "DefaultDistrict",
                DefaultFirstDeliveryDateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                DefaultDeliveryLogPath = "deliveryLog.json",
                DefaultDeliveryOrderPath = "filteredOrders.json",
                OrdersFilePath = "orders.json"
            };
            File.WriteAllText(path, JsonSerializer.Serialize(defaultConfig, new JsonSerializerOptions { WriteIndented = true }));
            return defaultConfig;
        }
    }

    public class Order
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        public string OrderId { get; set; }
        public double Weight { get; set; }
        public string District { get; set; }
        public DateTime DeliveryTime { get; set; }

        public static List<Order> LoadOrders(string path)
        {
            if (!File.Exists(path))
            {
                var ex = new FileNotFoundException($"Файл с заказами не найден: {path}");
                ex.Data.Add("Comment", "Ошибка поиска файла с заказами");
                throw ex;
            }

            try
            {
                var orders = JsonSerializer.Deserialize<List<Order>>(File.ReadAllText(path));
                return orders;
            }
            catch (JsonException ex)
            {
                ex.Data.Add("Comment", "Ошибка десериализации заказов");
                throw;
            }
        }

        public static List<Order> ValidateOrders(List<Order> orders)
        {
            var validOrders = new List<Order>();
            var errors = new List<string>();

            var duplicateOrderIds = orders.GroupBy(o => o.OrderId).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (duplicateOrderIds.Any())
            {
                foreach (var duplicateId in duplicateOrderIds)
                {
                    logger.Warn($"Обнаружен дублирующийся идентификатор заказа: {duplicateId}");
                }
            }
            var ordersWithoutDuplicates = orders.Where(o => !duplicateOrderIds.Contains(o.OrderId)).ToList();

            foreach (var order in ordersWithoutDuplicates)
            {
                var orderErrors = new List<string>();

                if (string.IsNullOrEmpty(order.OrderId))
                {
                    orderErrors.Add("Невалидный идентификатор заказа.");
                }

                if (order.Weight <= 0)
                {
                    orderErrors.Add("Некорректный вес заказа (должен быть больше 0).");
                }

                if (string.IsNullOrEmpty(order.District))
                {
                    orderErrors.Add("Невалидное название района заказа.");
                }

                if (order.DeliveryTime == default)
                {
                    orderErrors.Add("Некорректная дата доставки.");
                }

                if (orderErrors.Count > 0)
                {
                    errors.Add($"Заказ ID {order.OrderId ?? "(не указан)"}: {string.Join(", ", orderErrors)}");
                }
                else
                {
                    validOrders.Add(order);
                }
            }

            if (errors.Count > 0)
            {
                foreach (var error in errors)
                {
                    logger.Warn(error);
                }
            }

            return validOrders;
        }

        public static void SaveOrders(List<Order> orders, string path)
        {
            try
            {
                File.WriteAllText(path, JsonSerializer.Serialize(orders, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                ex.Data.Add("Comment", "Ошибка сохранения заказов");
                throw;
            }
        }
    }
}
