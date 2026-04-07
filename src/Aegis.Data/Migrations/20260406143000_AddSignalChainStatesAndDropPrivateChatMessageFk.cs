using System;
using Aegis.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aegis.Data.Migrations
{
    [DbContext(typeof(AegisDbContext))]
    [Migration("20260406143000_AddSignalChainStatesAndDropPrivateChatMessageFk")]
    public partial class AddSignalChainStatesAndDropPrivateChatMessageFk : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PrivateChats_Messages_LastMessageId",
                table: "PrivateChats");

            migrationBuilder.CreateTable(
                name: "SignalChainStates",
                columns: table => new
                {
                    Id = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    OwnerUserId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    PeerUserId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    RootKeyBase64 = table.Column<string>(type: "text", nullable: false),
                    SendingChainKeyBase64 = table.Column<string>(type: "text", nullable: false),
                    ReceivingChainKeyBase64 = table.Column<string>(type: "text", nullable: false),
                    NextSendingMessageNumber = table.Column<uint>(type: "integer", nullable: false),
                    NextReceivingMessageNumber = table.Column<uint>(type: "integer", nullable: false),
                    LastMessageKeyHash = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignalChainStates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SignalChainStates_OwnerUserId_PeerUserId",
                table: "SignalChainStates",
                columns: new[] { "OwnerUserId", "PeerUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SignalChainStates_UpdatedAt",
                table: "SignalChainStates",
                column: "UpdatedAt");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SignalChainStates");

            migrationBuilder.AddForeignKey(
                name: "FK_PrivateChats_Messages_LastMessageId",
                table: "PrivateChats",
                column: "LastMessageId",
                principalTable: "Messages",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}