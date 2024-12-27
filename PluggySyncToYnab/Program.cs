using Pluggy.SDK;
using Pluggy.SDK.Model;
using System.Globalization;
using YNAB.SDK.Model;
using static YNAB.SDK.Model.SaveTransaction;

namespace PluggySyncToYnab
{
    internal class Program
    {
        private static string _pluggyClientId = Environment.GetEnvironmentVariable("PLUGGY_CLIENT_ID");
        private static string _pluggyClientSecret = Environment.GetEnvironmentVariable("PLUGGY_CLIENT_SECRET");
        private static string _ynabBudgetId = Environment.GetEnvironmentVariable("YNAB_BUDGET_ID");
        private static string _ynabToken = Environment.GetEnvironmentVariable("YNAB_TOKEN");

        private static Guid _nubankPluggyAccountId = Guid.Parse(Environment.GetEnvironmentVariable("NUBANK_PLUGGY_ACCOUNT_ID"));
        private static Guid _nubankYnabAccountId = Guid.Parse(Environment.GetEnvironmentVariable("NUBANK_YNAB_ACCOUNT_ID"));
        private static Guid _readyToAssignCategoryId = Guid.Parse(Environment.GetEnvironmentVariable("READY_TO_ASSIGN_CATEGORY_ID"));

        private static PluggyAPI _pluggyClient = new(_pluggyClientId, _pluggyClientSecret);
        private static YNAB.SDK.API _ynabClient = new(_ynabToken);

        static async Task Main(string[] args)
        {
            DateTime today = DateTime.UtcNow;
            DateTime transactionsStartDate = today.AddDays(-1);

            Console.WriteLine("Started syncing.");

            TransactionParameters parameters = new()
            {
                DateFrom = transactionsStartDate,
                DateTo = today,
                Page = 1,
                PageSize = 100,
            };

            PageResults<Transaction> transactions = await _pluggyClient.FetchTransactions(_nubankPluggyAccountId, parameters);

            Console.WriteLine($"Found {transactions.Total} transactions.");

            List<SaveTransaction> ynabTransactions = new();

            foreach (var transaction in transactions.Results)
            {
                if (transaction.Type == TransactionType.DEBIT)
                {
                    SaveTransaction mappedTransaction = MapDebitTransaction(transaction);
                    Console.WriteLine($"{mappedTransaction.Date.ToString("dd/MM/yyyy")} | {mappedTransaction.PayeeName} | {mappedTransaction.Amount / 1000}");
                    ynabTransactions.Add(mappedTransaction);
                }

                if (transaction.Type == TransactionType.CREDIT)
                {
                    SaveTransaction mappedTransaction = MapCreditTransaction(transaction);
                    Console.WriteLine($"{mappedTransaction.Date.ToString("dd/MM/yyyy")} | {mappedTransaction.PayeeName} | {mappedTransaction.Amount / 1000}");
                    ynabTransactions.Add(mappedTransaction);
                }
            }

            _ynabClient.Transactions.CreateTransaction(_ynabBudgetId, new SaveTransactionsWrapper { Transactions = ynabTransactions });

            Console.WriteLine("Done!");
        }

        private static SaveTransaction MapDebitTransaction(Transaction transaction)
        {
            string payeeName = GetFormattedPayeeFromDescription(transaction.Description);

            return new SaveTransaction
            {
                AccountId = _nubankYnabAccountId,
                Date = transaction.Date,
                Amount = (long)(transaction.Amount * 1000),
                PayeeName = payeeName,
                Cleared = GetClearedStatus(transaction.Status),
                ImportId = transaction.Id.ToString(),
            };
        }

        private static SaveTransaction MapCreditTransaction(Transaction transaction)
        {
            string payeeName = GetFormattedPayeeFromDescription(transaction.Description);

            return new SaveTransaction
            {
                AccountId = _nubankYnabAccountId,
                Date = transaction.Date,
                Amount = (long)(transaction.Amount * 1000),
                PayeeName = payeeName,
                CategoryId = _readyToAssignCategoryId,
                Cleared = GetClearedStatus(transaction.Status),
                ImportId = transaction.Id.ToString(),
            };
        }

        private static string GetFormattedPayeeFromDescription(string description)
        {
            var payeeName = GetPayeeFromTransactionDescription(description, "Compra no débito|", "Transferência enviada|", "Transferência Recebida|");

            string capitalizedPayeeName = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(payeeName.ToLower());

            return capitalizedPayeeName;
        }

        private static string GetPayeeFromTransactionDescription(string description, params string[] prefixes)
        {
            foreach (var prefix in prefixes)
            {
                if (description.StartsWith(prefix))
                {
                    return description.Substring(prefix.Length).Trim();
                }
            }
            return description;
        }

        private static ClearedEnum GetClearedStatus(TransactionStatus? status) =>
            status == TransactionStatus.POSTED ? ClearedEnum.Cleared : ClearedEnum.Uncleared;
    }
}
