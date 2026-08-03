using Lamie.Application.Settings.Products.Commands;
using Lamie.Application.Settings.Products.Dtos;
using Lamie.Application.Settings.Products.Queries;
using Lamie.Application.Identity;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lamie.API.Controllers
{
    [ApiController]
    [Authorize(Policy = PermissionNames.ProductsView)]
    [Route("api/settings/products")]
    public class ProductController : ControllerBase
    {
        private readonly IMediator _mediator;

        public ProductController(IMediator mediator)
        {
            _mediator = mediator;
        }

        /// <summary>
        /// Admin: Tạo sản phẩm mới (multipart/form-data: ThumbnailFile, Images[].ImageFile)
        /// </summary>
        [HttpPost]
        [Authorize(Policy = PermissionNames.ProductsManage)]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> Create([FromForm] CreateProductCommand command, CancellationToken cancellationToken)
        {
            var productId = await _mediator.Send(command, cancellationToken);

            return CreatedAtAction(
                nameof(GetById),
                new { id = productId },
                new { id = productId }
            );
        }

        /// <summary>
        /// Admin: Lấy chi tiết sản phẩm (ví dụ minh họa)
        /// </summary>
        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken)
        {
            var result = await _mediator.Send(new GetProductByIdQuery(id), cancellationToken);
            return Ok(result);
        }

        /// <summary>
        /// Get All
        /// </summary>
        [HttpGet()]
        public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
        {
            var result = await _mediator.Send(new GetAllProductsQuery(), cancellationToken);
            return Ok(result);
        }

        /// <summary>
        /// Admin: Cập nhật sản phẩm (multipart/form-data khi có Images[].ImageFile)
        /// </summary>
        [HttpPut]
        [Authorize(Policy = PermissionNames.ProductsManage)]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> Update([FromForm] UpdateProductCommand command, CancellationToken cancellationToken)
        {
            await _mediator.Send(command, cancellationToken);
            return NoContent();
        }

        /// <summary>
        /// Admin: Cập nhật sản phẩm với id trên route, tương thích với FE Admin.
        /// </summary>
        [HttpPut("{id:int}")]
        [Authorize(Policy = PermissionNames.ProductsManage)]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UpdateById(
            int id,
            [FromForm] UpdateProductCommand command,
            CancellationToken cancellationToken)
        {
            if (command.Id > 0 && command.Id != id)
            {
                return BadRequest(new
                {
                    success = false,
                    code = "VALIDATION_ERROR",
                    message = "Validation failed",
                    errors = new Dictionary<string, string[]>
                    {
                        [nameof(command.Id)] = ["Route id and form id must match."]
                    }
                });
            }

            command.Id = id;
            command.UseFullReplacementSemantics();
            await _mediator.Send(command, cancellationToken);
            return NoContent();
        }

        /// <summary>
        /// Admin: Xóa sản phẩm
        /// </summary>
        [HttpDelete("{id:int}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
        {
            await _mediator.Send(new DeleteProductCommand(id), cancellationToken);
            return NoContent();
        }

        // Logic sanitize / build path đã được chuyển xuống Application layer (CreateProductHandler)
    }
}
