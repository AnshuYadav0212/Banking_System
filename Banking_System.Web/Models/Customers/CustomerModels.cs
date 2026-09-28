namespace Banking_System.Web.Models.Customers;

// Mirrors Banking_System.ApiService/DTOs/CustomerDirectoryDtos.cs. Every
// identifier arrives already masked from the API.
public sealed record CustomerAccountRow(
    string FullName,
    string MaskedCif,
    string MaskedPhone,
    string? MaskedEmail,
    int AccountCount,
    decimal TotalBalance,
    bool HasOnlineBanking,
    string Status);

public sealed record CustomerAccountPage(
    List<CustomerAccountRow> Items,
    int TotalCount,
    int Page,
    int PageSize);
