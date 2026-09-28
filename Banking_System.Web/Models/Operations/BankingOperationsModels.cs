namespace Banking_System.Web.Models.Operations;

// Mirrors Banking_System.ApiService/DTOs/BankingOperationsDtos.cs.
public sealed record AccountOperationRow(
    Guid AccountId,
    string MaskedAccountNumber,
    string CustomerName,
    string MaskedCif,
    decimal AvailableBalance,
    bool Active);

public sealed record CustomerOperationRow(
    Guid CustomerId,
    string MaskedCif,
    string Name,
    string MaskedPhone,
    bool Active,
    int AccountCount);
