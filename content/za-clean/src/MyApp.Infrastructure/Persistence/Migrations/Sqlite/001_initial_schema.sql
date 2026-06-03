CREATE TABLE "Orders" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Orders" PRIMARY KEY,
    "CustomerId" INTEGER NOT NULL,
    "Status" TEXT NOT NULL,
    "Total" TEXT NOT NULL
);

CREATE TABLE "OrderLines" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_OrderLines" PRIMARY KEY AUTOINCREMENT,
    "Sku" TEXT NOT NULL,
    "Quantity" INTEGER NOT NULL CHECK ("Quantity" > 0),
    "Price" TEXT NOT NULL,
    "OrderId" INTEGER NOT NULL,
    CONSTRAINT "FK_OrderLines_Orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES "Orders" ("Id") ON DELETE CASCADE
);

CREATE INDEX "IX_OrderLines_OrderId" ON "OrderLines" ("OrderId");
