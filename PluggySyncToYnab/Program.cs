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
            DateTime sevenDaysAgo = today.AddDays(-7);

            Console.WriteLine("Started syncing.");

            TransactionParameters parameters = new()
            {
                DateFrom = sevenDaysAgo,
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
                    ynabTransactions.Add(MapDebitTransaction(transaction));
                }

                if (transaction.Type == TransactionType.CREDIT)
                {
                    ynabTransactions.Add(MapCreditTransaction(transaction));
                }
            }

            try
            {
                if (ynabTransactions.Any())
                {
                    _ynabClient.Transactions.CreateTransaction(_ynabBudgetId, new SaveTransactionsWrapper { Transactions = ynabTransactions });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not add transactions to YNAB. Error: {ex}");
            }

            Console.WriteLine("Done!");
        }

        private static SaveTransaction MapDebitTransaction(Transaction transaction)
        {
            var payeeName = CleanDescription(transaction.Description, "Compra no débito|", "Transferência enviada|");

            return new SaveTransaction
            {
                AccountId = _nubankYnabAccountId,
                Date = transaction.Date,
                Amount = (long)(transaction.Amount * 1000),
                PayeeName = payeeName,
                Cleared = GetClearedStatus(transaction.Status),
            };
        }

        private static SaveTransaction MapCreditTransaction(Transaction transaction)
        {
            var payeeName = CleanDescription(transaction.Description, "Transferência Recebida|");

            return new SaveTransaction
            {
                AccountId = _nubankYnabAccountId,
                Date = transaction.Date,
                Amount = (long)(transaction.Amount * 1000),
                PayeeName = payeeName,
                CategoryId = _readyToAssignCategoryId,
                Cleared = GetClearedStatus(transaction.Status),
            };
        }

        private static string CleanDescription(string description, params string[] prefixes)
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
