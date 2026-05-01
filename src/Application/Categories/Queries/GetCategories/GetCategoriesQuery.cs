using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Expenses;
using MediatR;

namespace FinFlow.Application.Categories.Queries.GetCategories;

public sealed record GetCategoriesQuery(Guid TenantId, bool IncludeInactive = false) : IRequest<Result<IReadOnlyList<CategorySummary>>>;