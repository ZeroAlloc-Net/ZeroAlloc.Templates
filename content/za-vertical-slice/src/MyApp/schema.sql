CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
    "ProductVersion" TEXT NOT NULL
);

BEGIN TRANSACTION;
CREATE TABLE "Orders" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Orders" PRIMARY KEY,
    "CustomerId" INTEGER NOT NULL,
    "Status" TEXT NOT NULL,
    "Total" TEXT NOT NULL
);

CREATE TABLE "OrderLines" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_OrderLines" PRIMARY KEY AUTOINCREMENT,
    "Sku" TEXT NOT NULL,
    "Quantity" INTEGER NOT NULL,
    "Price" TEXT NOT NULL,
    "OrderId" INTEGER NOT NULL,
    CONSTRAINT "FK_OrderLines_Orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES "Orders" ("Id") ON DELETE CASCADE
);

CREATE INDEX "IX_OrderLines_OrderId" ON "OrderLines" ("OrderId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260511111619_InitialCreate', '10.0.7');

COMMIT;

