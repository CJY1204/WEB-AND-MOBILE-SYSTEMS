using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuestPDF.Fluent;
using RestaurantSystem.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using static RestaurantSystem.Models.ViewModels;

namespace RestaurantSystem.Controllers
{
    [Authorize]
    public class OrderController : Controller
    {
        private readonly RestaurantDbContext db;
        private const string CART_KEY = "OrderingCart"; // Must match CartController's constant
        private const string PENDING_OPTION_KEY = "Pending_DiningOption";
        private const string PENDING_TABLE_KEY = "Pending_TableId";
        private const string PENDING_PAYMENT_KEY = "Pending_PaymentMethod";
        private const string PENDING_ADDRESS_KEY = "Pending_Address";

        public OrderController(RestaurantDbContext db)
        {
            this.db = db;
        }

        // ==========================================
        // [GET/POST: Checkout] — choose Takeaway or Dine-in (+ table)
        // ==========================================
        [HttpGet]
        public IActionResult Checkout()
        {
            var cart = GetCartFromSession();
            if (!cart.Any())
            {
                TempData["Info"] = "Your cart is empty. Please add some dishes first.";
                return RedirectToAction("Cart", "Home");
            }

            var userId = GetCurrentUserId();
            var currentUser = userId.HasValue ? db.Users.FirstOrDefault(u => u.UserId == userId.Value) : null;

            var vm = new CheckoutVM
            {
                DiningOption = "",
                CartItems = cart,
                AvailableTables = db.Tables.Where(t => !t.IsOccupied).OrderBy(t => t.TableName).ToList(),
                Address = currentUser?.Address
            };
            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Checkout(CheckoutVM vm)
        {
            var cart = GetCartFromSession();
            if (!cart.Any())
            {
                TempData["Info"] = "Your cart is empty. Please add some dishes first.";
                return RedirectToAction("Cart", "Home");
            }

            if (vm.DiningOption != "DineIn" && vm.DiningOption != "Takeaway")
            {
                ModelState.AddModelError("DiningOption", "Please choose Takeaway or Dine-in.");
            }

            Table? selectedTable = null;
            if (vm.DiningOption == "DineIn")
            {
                selectedTable = vm.TableId.HasValue ? db.Tables.FirstOrDefault(t => t.TableId == vm.TableId.Value) : null;

                if (selectedTable == null)
                {
                    ModelState.AddModelError("TableId", "Please select a table for dine-in.");
                }
                else if (selectedTable.IsOccupied)
                {
                    ModelState.AddModelError("TableId", "Sorry, this table has just been occupied. Please pick another one.");
                }
            }
            else if (vm.DiningOption == "Takeaway")
            {
                if (string.IsNullOrWhiteSpace(vm.Address))
                {
                    ModelState.AddModelError("Address", "Please enter an address for your takeaway order.");
                }
            }

            if (!ModelState.IsValid)
            {
                vm.CartItems = cart;
                vm.AvailableTables = db.Tables.Where(t => !t.IsOccupied).OrderBy(t => t.TableName).ToList();
                return View(vm);
            }

            HttpContext.Session.SetString(PENDING_OPTION_KEY, vm.DiningOption);
            if (vm.DiningOption == "DineIn")
            {
                HttpContext.Session.SetInt32(PENDING_TABLE_KEY, selectedTable!.TableId);
                HttpContext.Session.Remove(PENDING_ADDRESS_KEY);
            }
            else
            {
                HttpContext.Session.Remove(PENDING_TABLE_KEY);
                HttpContext.Session.SetString(PENDING_ADDRESS_KEY, vm.Address ?? "");

                // Remember this address on the user's profile for next time
                var userId = GetCurrentUserId();
                if (userId.HasValue)
                {
                    var user = db.Users.FirstOrDefault(u => u.UserId == userId.Value);
                    if (user != null && user.Address != vm.Address)
                    {
                        user.Address = vm.Address;
                        db.SaveChanges();
                    }
                }
            }

            return RedirectToAction("Payment");
        }

        // ==========================================
        // [GET: Payment]
        // ==========================================
        [HttpGet]
        public IActionResult Payment()
        {
            var cart = GetCartFromSession();
            var diningOption = HttpContext.Session.GetString(PENDING_OPTION_KEY);

            if (!cart.Any() || string.IsNullOrEmpty(diningOption))
            {
                TempData["Info"] = "Please choose Takeaway or Dine-in before payment.";
                return RedirectToAction("Checkout");
            }

            string? tableName = null;
            if (diningOption == "DineIn")
            {
                var tableId = HttpContext.Session.GetInt32(PENDING_TABLE_KEY);
                tableName = db.Tables.FirstOrDefault(t => t.TableId == tableId)?.TableName;
            }

            var vm = new PaymentVM
            {
                PaymentMethod = "",
                CartItems = cart,
                DiningOption = diningOption,
                TableName = tableName
            };

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult PlaceOrder(PaymentVM vm)
        {
            var cart = GetCartFromSession();
            var diningOption = HttpContext.Session.GetString(PENDING_OPTION_KEY);

            if (!cart.Any() || string.IsNullOrEmpty(diningOption))
            {
                TempData["Info"] = "Your session has expired. Please checkout again.";
                return RedirectToAction("Checkout");
            }

            if (string.IsNullOrEmpty(vm.PaymentMethod))
            {
                ModelState.AddModelError("PaymentMethod", "Please select a payment method.");
            }

            int? tableId = null;
            Table? selectedTable = null;
            if (diningOption == "DineIn")
            {
                tableId = HttpContext.Session.GetInt32(PENDING_TABLE_KEY);
                selectedTable = tableId.HasValue ? db.Tables.FirstOrDefault(t => t.TableId == tableId.Value) : null;

                // Re-check right before payment, in case someone else grabbed the table meanwhile
                if (selectedTable == null || selectedTable.IsOccupied)
                {
                    ModelState.AddModelError("", "Sorry, the selected table is no longer available. Please choose another table.");
                }
            }

            // Re-check stock right before payment too — items may have sold out since being added to cart
            var stockError = CheckStockAvailability(cart);
            if (stockError != null)
            {
                ModelState.AddModelError("", stockError);
            }

            if (!ModelState.IsValid)
            {
                vm.CartItems = cart;
                vm.DiningOption = diningOption;
                vm.TableName = selectedTable?.TableName;
                return View("Payment", vm);
            }

            if (vm.PaymentMethod == "EWallet")
            {
                HttpContext.Session.SetString(PENDING_PAYMENT_KEY, vm.PaymentMethod);
                return RedirectToAction("EWalletCheckout");
            }

            var address = HttpContext.Session.GetString(PENDING_ADDRESS_KEY);
            var order = CreateOrder(diningOption, tableId, vm.PaymentMethod, cart, address);
            ClearOrderingSession();

            TempData["Info"] = "Payment successful! Your order has been placed.";
            return RedirectToAction("Confirmation", new { id = order.OrderId });
        }

        // ==========================================
        // [GET/POST: E-Wallet mock checkout]
        // ==========================================
        [HttpGet]
        public IActionResult EWalletCheckout()
        {
            var cart = GetCartFromSession();
            var diningOption = HttpContext.Session.GetString(PENDING_OPTION_KEY);
            var paymentMethod = HttpContext.Session.GetString(PENDING_PAYMENT_KEY);

            if (!cart.Any() || string.IsNullOrEmpty(diningOption) || paymentMethod != "EWallet")
            {
                TempData["Info"] = "Please choose a payment method first.";
                return RedirectToAction("Checkout");
            }

            string? tableName = null;
            if (diningOption == "DineIn")
            {
                var tableId = HttpContext.Session.GetInt32(PENDING_TABLE_KEY);
                tableName = db.Tables.FirstOrDefault(t => t.TableId == tableId)?.TableName;
            }

            var vm = new PaymentVM
            {
                CartItems = cart,
                DiningOption = diningOption,
                PaymentMethod = paymentMethod ?? "",
                TableName = tableName
            };

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult EWalletConfirm()
        {
            var cart = GetCartFromSession();
            var diningOption = HttpContext.Session.GetString(PENDING_OPTION_KEY);
            var paymentMethod = HttpContext.Session.GetString(PENDING_PAYMENT_KEY);

            if (!cart.Any() || string.IsNullOrEmpty(diningOption) || paymentMethod != "EWallet")
            {
                TempData["Info"] = "Your session has expired. Please checkout again.";
                return RedirectToAction("Checkout");
            }

            int? tableId = null;
            if (diningOption == "DineIn")
            {
                tableId = HttpContext.Session.GetInt32(PENDING_TABLE_KEY);
                var table = tableId.HasValue ? db.Tables.FirstOrDefault(t => t.TableId == tableId.Value) : null;
                if (table == null || table.IsOccupied)
                {
                    TempData["Info"] = "Sorry, the selected table is no longer available. Please checkout again.";
                    return RedirectToAction("Checkout");
                }
            }

            var stockError = CheckStockAvailability(cart);
            if (stockError != null)
            {
                TempData["Info"] = stockError + " Please review your cart.";
                return RedirectToAction("Cart", "Home");
            }

            var address = HttpContext.Session.GetString(PENDING_ADDRESS_KEY);
            var order = CreateOrder(diningOption, tableId, paymentMethod, cart, address);
            ClearOrderingSession();

            TempData["Info"] = "E-Wallet payment successful! Your order has been placed.";
            return RedirectToAction("Confirmation", new { id = order.OrderId });
        }

        // ==========================================
        // [GET: Confirmation]
        // ==========================================
        [HttpGet]
        public IActionResult Confirmation(int id)
        {
            var summary = BuildOrderSummary(id);
            if (summary == null)
            {
                return NotFound();
            }

            return View(summary);
        }

        // ==========================================
        // [GET: History] — my past orders, optionally filtered by status
        // ==========================================
        [HttpGet]
        public IActionResult History(string status = "ALL")
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return RedirectToAction("Login", "Account");
            }

            var query = db.Orders.Where(o => o.UserId == userId.Value);

            if (!string.IsNullOrEmpty(status) && status.ToUpper() != "ALL")
            {
                query = query.Where(o => o.OrderStatus.ToUpper() == status.ToUpper());
            }

            var orderIds = query.OrderByDescending(o => o.OrderDate).Select(o => o.OrderId).ToList();
            var summaries = orderIds.Select(BuildOrderSummary).Where(o => o != null).ToList();

            ViewBag.CurrentStatus = string.IsNullOrEmpty(status) ? "ALL" : status;
            return View(summaries);
        }

        // ==========================================
        // [GET: Details]
        // ==========================================
        [HttpGet]
        public IActionResult Details(int id)
        {
            var summary = BuildOrderSummary(id);
            if (summary == null)
            {
                return NotFound();
            }

            return View(summary);
        }

        // ==========================================
        // [POST: Cancel]
        // ==========================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Cancel(int id)
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return RedirectToAction("Login", "Account");
            }

            var order = db.Orders.FirstOrDefault(o => o.OrderId == id && o.UserId == userId.Value);
            if (order == null)
            {
                return NotFound();
            }

            if (order.OrderStatus != "PENDING")
            {
                TempData["Info"] = "Sorry, this order can no longer be cancelled as it is already being prepared.";
                return RedirectToAction("Details", new { id });
            }

            order.OrderStatus = "CANCELLED";

            // If this order occupied a table, free it up immediately
            if (order.TableId.HasValue)
            {
                var table = db.Tables.FirstOrDefault(t => t.TableId == order.TableId.Value);
                if (table != null)
                {
                    table.IsOccupied = false;
                }
            }

            // Restore stock for every item in this order
            var cancelledItems = db.OrderDetails.Where(od => od.OrderId == id).ToList();
            foreach (var detail in cancelledItems)
            {
                var product = db.Products.FirstOrDefault(p => p.ProductId == detail.ProductId);
                if (product != null)
                {
                    product.StockQuantity += detail.Quantity;
                }
            }

            db.SaveChanges();

            TempData["Info"] = "Your order has been cancelled.";
            return RedirectToAction("History");
        }

        // ==========================================
        // [POST: CheckOutTable] — customer manually frees up their own table
        // after finishing their meal. Only affects Table.IsOccupied; does not
        // change the order's own status.
        // ==========================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult CheckOutTable(int orderId)
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return RedirectToAction("Login", "Account");
            }

            var order = db.Orders.FirstOrDefault(o => o.OrderId == orderId && o.UserId == userId.Value);
            if (order == null)
            {
                return NotFound();
            }

            if (!order.TableId.HasValue)
            {
                TempData["Info"] = "This order is not a dine-in order.";
                return RedirectToAction("History");
            }

            var table = db.Tables.FirstOrDefault(t => t.TableId == order.TableId.Value);
            if (table != null)
            {
                table.IsOccupied = false;
                db.SaveChanges();
                TempData["Info"] = $"Thanks for dining with us! {table.TableName} has been checked out.";
            }

            return RedirectToAction("History");
        }

        // ==========================================
        // [GET: DownloadReceipt] — PDF via QuestPDF
        // ==========================================
        [HttpGet]
        public IActionResult DownloadReceipt(int id)
        {
            var summary = BuildOrderSummary(id);
            if (summary == null)
            {
                return NotFound();
            }

            var pdfBytes = QuestPDF.Fluent.Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(QuestPDF.Helpers.PageSizes.A5);
                    page.Margin(30);
                    page.DefaultTextStyle(x => x.FontSize(10));

                    page.Header().Column(col =>
                    {
                        col.Item().Text("BITE Restaurant").FontSize(20).Bold();
                        col.Item().Text("E-Receipt").FontSize(12).FontColor(QuestPDF.Helpers.Colors.Grey.Darken1);
                        col.Item().PaddingTop(5).LineHorizontal(1);
                    });

                    page.Content().PaddingVertical(15).Column(col =>
                    {
                        col.Item().Text($"Order #{summary.OrderId}").Bold().FontSize(13);
                        col.Item().Text($"Date: {summary.OrderDate:dd MMM yyyy, HH:mm}");
                        col.Item().Text($"Dining Option: {(summary.DiningOption == "DineIn" ? $"Dine-in ({summary.TableName})" : "Takeaway")}");
                        col.Item().Text($"Payment Method: {summary.PaymentMethod}");
                        col.Item().Text($"Status: {summary.Status}");

                        col.Item().PaddingTop(10).LineHorizontal(0.5f);

                        col.Item().PaddingTop(10).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(4);
                                columns.RelativeColumn(1);
                                columns.RelativeColumn(2);
                                columns.RelativeColumn(2);
                            });

                            table.Header(header =>
                            {
                                header.Cell().Text("Dish").Bold();
                                header.Cell().Text("Qty").Bold();
                                header.Cell().AlignRight().Text("Unit Price").Bold();
                                header.Cell().AlignRight().Text("Subtotal").Bold();
                                header.Cell().ColumnSpan(4).PaddingBottom(3).LineHorizontal(0.5f);
                            });

                            foreach (var item in summary.Items)
                            {
                                table.Cell().Text(item.ProductName);
                                table.Cell().Text(item.Quantity.ToString());
                                table.Cell().AlignRight().Text($"RM {item.UnitPrice:F2}");
                                table.Cell().AlignRight().Text($"RM {item.SubTotal:F2}");
                            }
                        });

                        col.Item().PaddingTop(10).LineHorizontal(0.5f);

                        col.Item().PaddingTop(10).AlignRight().Text($"Total Paid: RM {summary.TotalAmount:F2}")
                            .Bold().FontSize(13);
                    });

                    page.Footer().AlignCenter().Text("Thank you for dining with BITE Restaurant!")
                        .FontSize(9).FontColor(QuestPDF.Helpers.Colors.Grey.Darken1);
                });
            }).GeneratePdf();

            return File(pdfBytes, "application/pdf", $"Receipt_Order{summary.OrderId}.pdf");
        }

        // ==========================================
        // Private helpers
        // ==========================================

        // Resolve the current logged-in username (from the auth cookie claim) into a real UserId
        private int? GetCurrentUserId()
        {
            if (User?.Identity == null || !User.Identity.IsAuthenticated)
            {
                return null;
            }

            var username = User.Identity.Name;
            var user = db.Users.FirstOrDefault(u => u.Username == username);
            return user?.UserId;
        }

        // Checks every cart line against current DB stock right before an order is
        // actually created. Returns null if everything is fine, or a user-facing
        // error message describing the first problem found.
        private string? CheckStockAvailability(List<CartItemVM> cart)
        {
            foreach (var item in cart)
            {
                var product = db.Products.FirstOrDefault(p => p.ProductId == item.ProductId);
                if (product == null)
                {
                    return $"{item.ProductName} is no longer available.";
                }
                if (product.StockQuantity < item.Quantity)
                {
                    return product.StockQuantity == 0
                        ? $"Sorry, {product.ProductName} is now out of stock."
                        : $"Sorry, only {product.StockQuantity} of {product.ProductName} left in stock.";
                }
            }
            return null;
        }

        private Order CreateOrder(string diningOption, int? tableId, string paymentMethod, List<CartItemVM> cart, string? address = null)
        {
            var userId = GetCurrentUserId();

            var order = new Order
            {
                UserId = userId!.Value,
                OrderDate = DateTime.Now,
                TotalAmount = cart.Sum(i => i.Total),
                OrderStatus = "PENDING",
                DiningOption = diningOption,
                TableId = tableId,
                PaymentMethod = paymentMethod,
                DeliveryAddress = address
            };

            db.Orders.Add(order);
            db.SaveChanges(); // save first so order.OrderId is generated

            // Snapshot each cart line into OrderDetail — ProductId stays a real FK,
            // PriceAtOrder locks in the price at the moment of purchase
            foreach (var item in cart)
            {
                db.OrderDetails.Add(new OrderDetail
                {
                    OrderId = order.OrderId,
                    ProductId = item.ProductId,
                    Quantity = item.Quantity,
                    PriceAtOrder = item.Price
                });

                // Deduct stock now that the order is actually being placed
                // (stock is intentionally NOT reserved just for sitting in a cart)
                var product = db.Products.FirstOrDefault(p => p.ProductId == item.ProductId);
                if (product != null)
                {
                    product.StockQuantity -= item.Quantity;
                    if (product.StockQuantity < 0) product.StockQuantity = 0; // safety clamp
                }
            }

            // Dine-in: mark the selected table as occupied
            if (tableId.HasValue)
            {
                var table = db.Tables.FirstOrDefault(t => t.TableId == tableId.Value);
                if (table != null)
                {
                    table.IsOccupied = true;
                }
            }

            db.SaveChanges();
            return order;
        }

        private void ClearOrderingSession()
        {
            HttpContext.Session.Remove(CART_KEY);
            HttpContext.Session.Remove(PENDING_OPTION_KEY);
            HttpContext.Session.Remove(PENDING_TABLE_KEY);
            HttpContext.Session.Remove(PENDING_PAYMENT_KEY);
            HttpContext.Session.Remove(PENDING_ADDRESS_KEY);
        }

        private OrderSummaryVM? BuildOrderSummary(int orderId)
        {
            var userId = GetCurrentUserId();
            if (userId == null) return null;

            var order = db.Orders.FirstOrDefault(o => o.OrderId == orderId && o.UserId == userId.Value);
            if (order == null) return null;

            string? tableName = null;
            bool tableStillOccupied = false;
            if (order.TableId.HasValue)
            {
                var table = db.Tables.FirstOrDefault(t => t.TableId == order.TableId.Value);
                tableName = table?.TableName;
                tableStillOccupied = table?.IsOccupied ?? false;
            }

            var items = (from od in db.OrderDetails
                         join p in db.Products on od.ProductId equals p.ProductId
                         where od.OrderId == orderId
                         select new OrderItemLineVM
                         {
                             ProductName = p.ProductName,
                             Quantity = od.Quantity,
                             UnitPrice = od.PriceAtOrder
                         }).ToList();

            return new OrderSummaryVM
            {
                OrderId = order.OrderId,
                OrderDate = order.OrderDate,
                Status = order.OrderStatus,
                TotalAmount = order.TotalAmount,
                DiningOption = order.DiningOption ?? "",
                TableName = tableName,
                TableStillOccupied = tableStillOccupied,
                PaymentMethod = order.PaymentMethod ?? "",
                DeliveryAddress = order.DeliveryAddress,
                Items = items
            };
        }

        private List<CartItemVM> GetCartFromSession()
        {
            var sessionData = HttpContext.Session.GetString(CART_KEY);
            if (string.IsNullOrEmpty(sessionData))
            {
                return new List<CartItemVM>();
            }
            return JsonSerializer.Deserialize<List<CartItemVM>>(sessionData) ?? new List<CartItemVM>();
        }
    }
}