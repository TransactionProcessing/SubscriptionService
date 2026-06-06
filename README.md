# SubscriptionService

Seed the database with `scripts/seed.sql` after migrations if you want a sample subscription row.

Example connection strings for a local setup:

- `ConnectionStrings:KurrentDb`: `esdb://localhost:2113?Tls=false`
- `ConnectionStrings:SubscriptionServiceDb`: `Server=localhost,1433;Database=CatchupService;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False`
