using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using ProductAPI.Models;
using System.Collections;

namespace ProductAPI.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class ProductsController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IMemoryCache _cache;

        public ProductsController(AppDbContext context, IMemoryCache cache)
        {
            _context = context;
            _cache = cache;
        }

        [HttpGet]
        public async Task<IActionResult> GetProducts()
        {
            const string cacheKey = "ProductList";

            // Try to get the product list from the cache
            if (!_cache.TryGetValue(cacheKey, out List<Product> productList))
            {
                // If the product list is not in the cache, get it from the database
                productList = await _context.Products.ToListAsync();

                var cacheOptions = new MemoryCacheEntryOptions
                {
                    // Set the absolute expiration time (e.g., 5 minutes)
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),

                    // Set a priority to prevent cache eviction under memory pressure
                    Priority = CacheItemPriority.High
                };

                _cache.Set(cacheKey, productList, cacheOptions);
            }

            var products = productList.Select(p => new
            {
                p.Id,
                p.Name,
                p.Available,
                p.Price
            }).ToList();

            return Ok(products);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<Product>> GetProductById(int id)
        {
            string cacheKey = $"Product_{id}";

            // Try to get the product from the cache
            if (!_cache.TryGetValue(cacheKey, out Product product))
            {
                // If the product is not in the cache, get it from the database
                product = await _context.Products.FindAsync(id);

                if (product == null)
                    return NotFound();

                var cacheOptions = new MemoryCacheEntryOptions
                {
                    // Set the absolute expiration time (e.g., 5 minutes)
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),

                    // Set a priority to prevent cache eviction under memory pressure
                    Priority = CacheItemPriority.High
                };

                _cache.Set(cacheKey, product, cacheOptions);
            }

            return Ok(product);
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> AddProduct([FromBody] Product newProduct)
        {
            var existingProduct = await _context.Products.FirstOrDefaultAsync(p => p.Name == newProduct.Name);

            if (existingProduct != null)
                return Conflict("A product with the same name already exists.");

            _context.Products.Add(newProduct);
            await _context.SaveChangesAsync();

            // Invalidate the cache
            _cache.Remove("ProductList");
            _cache.Remove($"Product_{newProduct.Id}");

            return CreatedAtAction(nameof(GetProductById), new { id = newProduct.Id }, newProduct);
        }

        [HttpPut("{id}")]
        [Authorize]
        public async Task<IActionResult> UpdateProduct(int id, [FromBody] Product updatedProduct)
        {
            var existingProduct = await _context.Products.FindAsync(id);

            if (existingProduct == null)
                return NotFound();

            if (!StructuralComparisons.StructuralEqualityComparer.Equals(existingProduct.RowVersion, updatedProduct.RowVersion))
            {
                // This is the case when the product has changed since sending it to the client
                ModelState.AddModelError("RowVersion", "The record you attempted to update was modified by another user");
                return Conflict(ModelState);
            }

            existingProduct.Name = updatedProduct.Name;
            existingProduct.Price = updatedProduct.Price;
            existingProduct.Available = updatedProduct.Available;
            existingProduct.Description = updatedProduct.Description;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException ex)
            {
                var entry = ex.Entries.Single();
                var databaseValues = (Product)entry.GetDatabaseValues().ToObject();
                var clientValues = (Product)entry.Entity;

                if (databaseValues.RowVersion != clientValues.RowVersion)
                {
                    // This is the case when the product has changed since retrieving it from DB
                    ModelState.AddModelError("RowVersion", "The record you attempted to update was modified by another user");
                    return Conflict(ModelState);
                }

                // If there are other concurrency conflicts not related to RowVersion, rethrow the exception
                throw;
            }

            // Invalidate the cache
            _cache.Remove("ProductList");
            _cache.Remove($"Product_{id}");

            return NoContent();
        }

        [HttpDelete("{id}")]
        [Authorize]
        public async Task<IActionResult> DeleteProduct(int id)
        {
            var existingProduct = await _context.Products.FindAsync(id);

            if (existingProduct == null)
                return NotFound();

            _context.Products.Remove(existingProduct);

            await _context.SaveChangesAsync();

            // Invalidate the cache
            _cache.Remove("ProductList");
            _cache.Remove($"Product_{id}");

            return NoContent();
        }
    }
}