using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;

namespace RestaurantSystem.Models
{
    // ==========================================
    // 1. DbContext
    // ==========================================
    public class RestaurantDbContext : DbContext
    {
        public RestaurantDbContext(DbContextOptions<RestaurantDbContext> options) : base(options)
        {
        }

        public DbSet<User> Users { get; set; }
        public DbSet<Table> Tables { get; set; }
        public DbSet<Category> Categories { get; set; }
        public DbSet<Product> Products { get; set; }
        public DbSet<Order> Orders { get; set; }
        public DbSet<OrderDetail> OrderDetails { get; set; }
        public DbSet<Favorite> Favorites { get; set; }

        // NOTE: Data Annotations can't express "block deleting a Category that still
        // has Products" on their own, so Fluent API is used here as a deliberate,
        // narrow exception (matches AdminController.DeleteCategory's own check).
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Product>()
                .HasOne(p => p.Category)
                .WithMany(c => c.Products)
                .HasForeignKey(p => p.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }

    // ==========================================
    // 2. Entity Models with Full Validation
    // ==========================================

    public class User
    {
        [Key]
        public int UserId { get; set; }

        [Required(ErrorMessage = "Please enter username")]
        [StringLength(50, ErrorMessage = "Username cannot exceed 50 characters")]
        public required string Username { get; set; }

        [Required(ErrorMessage = "Please enter email address")]
        [EmailAddress(ErrorMessage = "Invalid email address format")]
        [StringLength(100, ErrorMessage = "Email cannot exceed 100 characters")]
        public required string Email { get; set; }

        [Required(ErrorMessage = "Please enter password")]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "Password must be between 6 and 100 characters")]
        [DataType(DataType.Password)]
        public required string Password { get; set; }

        [Required(ErrorMessage = "Please enter full name")]
        [StringLength(100, ErrorMessage = "Full name cannot exceed 100 characters")]
        public required string FullName { get; set; }

        [Phone(ErrorMessage = "Invalid phone number format")]
        [StringLength(20)]
        public required string PhoneNumber { get; set; }

        [Required]
        [StringLength(20)]
        public string Role { get; set; } = "MEMBER"; // "ADMIN" or "MEMBER"

        // Password Recovery (Email) support — nullable, only populated while a reset request is pending
        [StringLength(200)]
        public string? PasswordResetToken { get; set; }

        public DateTime? PasswordResetTokenExpiry { get; set; }

        // Profile photo — nullable, falls back to a default avatar in the UI when empty
        [StringLength(250)]
        public string? ProfilePhotoUrl { get; set; }

        // Admin can block/unblock a MEMBER account (see AdminController.ToggleBlock).
        // ADMIN accounts can never be blocked — enforced in the controller, not here.
        [Required]
        public bool IsBlocked { get; set; } = false;

        // Saved shipping/pickup address for Takeaway orders — one per account,
        // auto-filled at Checkout and kept up to date whenever the customer enters a new one.
        [StringLength(250)]
        public string? Address { get; set; }
    }

    public class Table
    {
        [Key]
        public int TableId { get; set; }

        [Required]
        [StringLength(50)]
        public required string TableName { get; set; }

        [Required]
        public bool IsOccupied { get; set; } = false;
    }

    public class Category
    {
        [Key]
        public int CategoryId { get; set; }

        [Required(ErrorMessage = "Please enter category name")]
        [StringLength(50, ErrorMessage = "Category name cannot exceed 50 characters")]
        public required string CategoryName { get; set; }

        // Navigation collection — no DB change, EF Core wires this up using the
        // existing Product.CategoryId FK. Lets Admin's .Include(p => p.Category) work.
        public List<Product>? Products { get; set; }
    }

    public class Product
    {
        [Key]
        public int ProductId { get; set; }

        [Required(ErrorMessage = "Please enter product name")]
        [StringLength(100, ErrorMessage = "Product name cannot exceed 100 characters")]
        public required string ProductName { get; set; }

        [StringLength(500, ErrorMessage = "Description cannot exceed 500 characters")]
        public string? Description { get; set; }

        [Required(ErrorMessage = "Please enter product price")]
        [Range(0.01, 999.99, ErrorMessage = "Price must be between 0.01 and 999.99")]
        [Column(TypeName = "decimal(18, 2)")]
        public decimal Price { get; set; }

        // Nullable on purpose: CreateProduct's form uploads the actual image via a
        // separate IFormFile, not this text field, so it's null at model-binding
        // time — a non-nullable string here would trip the "implicit required"
        // trap and block every product creation with "The ImageUrl field is required."
        [StringLength(250)]
        public string? ImageUrl { get; set; }

        [Required(ErrorMessage = "Please select a category")]
        public int CategoryId { get; set; }

        // Navigation property for the existing CategoryId FK — no DB change,
        // just wires up EF Core so .Include(p => p.Category) works.
        // [BindNever] stops ASP.NET Core from trying to model-bind this from
        // form data when a Product is posted (only CategoryId should bind).
        [ForeignKey("CategoryId")]
        [BindNever]
        public Category? Category { get; set; }

        // Available stock — decremented when an order is actually placed,
        // restored if that order is later cancelled. See OrderController.
        [Required]
        [Range(0, int.MaxValue, ErrorMessage = "Stock cannot be negative")]
        public int StockQuantity { get; set; } = 50;
    }

    public class Order
    {
        [Key]
        public int OrderId { get; set; }

        [Required]
        public int UserId { get; set; }

        public int? TableId { get; set; } // nullable = takeaway order

        [Required]
        [DataType(DataType.DateTime)]
        public DateTime OrderDate { get; set; } = DateTime.Now;

        [Required]
        [Column(TypeName = "decimal(18, 2)")]
        public decimal TotalAmount { get; set; }

        [Required]
        [StringLength(20)]
        public string OrderStatus { get; set; } = "PENDING"; // PENDING, COMPLETED, CANCELLED

        // ------------------------------------------
        // Checkout details (added for the Order/Checkout module)
        // ------------------------------------------
        [StringLength(20)]
        public string? DiningOption { get; set; } // "Takeaway" or "DineIn"

        [StringLength(30)]
        public string? PaymentMethod { get; set; } // e.g. "Cash", "EWallet"

        // Snapshot of the address used for THIS order (only set for Takeaway).
        // Kept separate from User.Address so editing the saved address later
        // doesn't rewrite the history of past orders.
        [StringLength(250)]
        public string? DeliveryAddress { get; set; }
    }

    public class OrderDetail
    {
        [Key]
        public int OrderDetailId { get; set; }

        [Required]
        public int OrderId { get; set; }

        [Required]
        public int ProductId { get; set; }

        [Required(ErrorMessage = "Quantity is required")]
        [Range(1, 100, ErrorMessage = "Quantity must be between 1 and 100")]
        public int Quantity { get; set; }

        [Required]
        [Column(TypeName = "decimal(18, 2)")]
        public decimal PriceAtOrder { get; set; } // snapshot of the price at order time
    }

    // ==========================================
    // Favorite — records which member favorited which product, persisted forever
    // ==========================================
    public class Favorite
    {
        [Key]
        public int FavoriteId { get; set; }

        [Required]
        public int UserId { get; set; }

        [Required]
        public int ProductId { get; set; }

        [Required]
        [DataType(DataType.DateTime)]
        public DateTime AddedDate { get; set; } = DateTime.Now;
    }
}