using Lamie.API.Services;
using Lamie.Application.Common.Exceptions;
using Lamie.Application.Expenses;
using Lamie.Application.Finance;
using Lamie.Domain.Entities;
using Lamie.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lamie.Tests.Finance;

public sealed class FinanceOperationsTests
{
    [Fact]
    public async Task PurchasePaymentsStocktakeDebtCloseAndReopenUsePersistedLedgers()
    {
        var databaseName = $"LamieFinance_{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer($"Server=(localdb)\\mssqllocaldb;Database={databaseName};Trusted_Connection=True;MultipleActiveResultSets=true")
            .UseSnakeCaseNamingConvention().Options;
        await using var db = new AppDbContext(options);
        try
        {
            await db.Database.EnsureCreatedAsync();
            var clock = new FixedTimeProvider(new DateTimeOffset(2026, 9, 30, 8, 0, 0, TimeSpan.Zero));
            var finance = new FinanceOperationsService(db, clock, new HttpContextAccessor());
            var period = await finance.GetOrCreatePeriodAsync(new SaveFinancePeriodRequest(
                new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), 4_000_000), default);

            var purchaseId = await finance.CreatePurchaseAsync(new SaveMaterialPurchaseRequest(
                new DateOnly(2026, 9, 5), new DateOnly(2026, 9, 5), "Chợ hoa",
                "Sạp A1", null, DocumentReviewStatus.NeedsReview, TaxEligibilityStatus.NotAssessed,
                "PM-001", null, null,
                [new PurchaseLineRequest(MaterialGroup.FreshFlowers, "Hoa tươi", 1_000_000)]), default);
            await finance.AddPurchasePaymentAsync(purchaseId, new AddFinancePaymentRequest(
                400_000, new DateTimeOffset(2026, 9, 6, 3, 0, 0, TimeSpan.Zero),
                PaymentMethod.BankTransfer, "purchase-payment-1", null), default);

            var category = new ExpenseCategory("Vận hành", null, 10, true, clock.GetUtcNow().UtcDateTime);
            db.ExpenseCategories.Add(category); await db.SaveChangesAsync();
            var expenses = new ExpenseService(db, clock, finance);
            var expenseId = await expenses.CreateAsync(new CreateExpenseRequest(category.Id,
                new DateOnly(2026, 9, 10), 500_000, "Chi phí phát sinh", null), default);
            await finance.AddExpensePaymentAsync(expenseId, new AddFinancePaymentRequest(
                100_000, new DateTimeOffset(2026, 9, 11, 3, 0, 0, TimeSpan.Zero),
                PaymentMethod.Cash, "expense-payment-1", null), default);

            var versionBeforeRejectedWrites = (await finance.GetPeriodAsync(period.Id, default)).DataVersion;
            await Assert.ThrowsAsync<ConflictException>(() => finance.AddPurchasePaymentAsync(purchaseId,
                new AddFinancePaymentRequest(600_001, new DateTimeOffset(2026, 9, 12, 3, 0, 0, TimeSpan.Zero),
                    PaymentMethod.BankTransfer, "purchase-overpayment", null), default));
            await Assert.ThrowsAsync<ConflictException>(() => expenses.UpdateAsync(expenseId,
                new UpdateExpenseRequest(category.Id, new DateOnly(2026, 9, 10), 99_999,
                    "Chi phí phát sinh", null), default));
            Assert.Equal(versionBeforeRejectedWrites,
                (await finance.GetPeriodAsync(period.Id, default)).DataVersion);

            await finance.SaveStocktakeAsync(period.Id, new SaveStocktakeRequest([
                new StocktakeLineRequest(MaterialGroup.FreshFlowers, 3_000_000, 4_000_000, 0, 0, false, null),
                new StocktakeLineRequest(MaterialGroup.Supplies, 0, 0, 0, 0, false, null),
                new StocktakeLineRequest(MaterialGroup.ContainersAndAccessories, 0, 0, 0, 0, false, null)
            ]), default);
            await finance.ConfirmStocktakeAsync(period.Id, default);
            await finance.ConfirmShippingAsync(period.Id, default);
            await finance.ConfirmExpensesAsync(period.Id, default);

            var summary = await finance.GetSummaryAsync(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), default);
            Assert.Equal(600_000, summary.SupplierDebt);
            Assert.Equal(400_000, summary.OperatingExpenseDebt);
            Assert.Equal(500_000, summary.CashOut);
            Assert.Equal(3_500_000, summary.ClosingCashBalance);
            Assert.Equal(0, summary.MaterialProductCogs);

            await finance.ClosePeriodAsync(period.Id, default);
            await Assert.ThrowsAsync<ConflictException>(() => finance.CreatePurchaseAsync(new SaveMaterialPurchaseRequest(
                new DateOnly(2026, 9, 20), new DateOnly(2026, 9, 20), "Nhà cung cấp",
                null, null, DocumentReviewStatus.Missing, TaxEligibilityStatus.NotAssessed,
                null, null, null, [new PurchaseLineRequest(MaterialGroup.Supplies, null, 100_000)]), default));

            await finance.ReopenPeriodAsync(period.Id, new ReopenFinancePeriodRequest("Bổ sung phiếu mua bị thiếu"), default);
            var reopened = await finance.GetPeriodAsync(period.Id, default);
            Assert.Equal(FinancePeriodStatus.Draft, reopened.Status);
            Assert.Contains(reopened.History, item => item.Kind == FinancePeriodEventKind.Reopened);
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
