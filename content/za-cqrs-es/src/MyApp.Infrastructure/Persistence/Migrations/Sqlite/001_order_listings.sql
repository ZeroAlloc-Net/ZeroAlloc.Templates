CREATE TABLE IF NOT EXISTS "order_listings" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_order_listings" PRIMARY KEY,
    "CustomerId" TEXT NOT NULL,
    "Status" TEXT NOT NULL,
    "Total" TEXT NOT NULL,
    "Currency" TEXT NOT NULL
);
