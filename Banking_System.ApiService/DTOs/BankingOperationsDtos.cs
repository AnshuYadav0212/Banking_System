namespace Banking_System.ApiService.DTOs;

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

public sealed record SetStatusRequest(bool Active);
