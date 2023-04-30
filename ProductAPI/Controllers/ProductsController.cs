using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductAPI.Models;
using System.Collections;

namespace ProductAPI.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class ProductsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public ProductsController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetProducts()
        {
            var products = await _context.Products.Select(p => new
            {
                p.Id,
                p.Name,
                p.Available,
                p.Price
            }).ToListAsync();

            return Ok(products);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetProductById(int id)
        {
            var product = await _context.Products.FindAsync(id);

            if (product == null)
                return NotFound();

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
                    ModelState.AddModelError("RowVersion", "The record you attempted to update was modified by another user");
                    return Conflict(ModelState);
                }

                // If there are other concurrency conflicts not related to RowVersion, rethrow the exception.
                throw;
            }

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

            return NoContent();
        }
    }
}