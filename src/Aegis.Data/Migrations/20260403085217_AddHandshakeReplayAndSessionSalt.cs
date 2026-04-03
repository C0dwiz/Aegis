using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aegis.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHandshakeReplayAndSessionSalt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HandshakeReplayEntries",
                columns: table => new
                {
                    Id = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    AppId = table.Column<int>(type: "integer", nullable: false),
                    NonceHash = table.Column<string>(type: "text", nullable: false),
                    FirstSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SourceIp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HandshakeReplayEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SessionSaltStates",
                columns: table => new
                {
                    Id = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    SessionId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    CurrentSalt = table.Column<long>(type: "bigint", nullable: false),
                    PreviousSalt = table.Column<long>(type: "bigint", nullable: true),
                    RotatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    PreviousSaltValidUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionSaltStates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HandshakeReplayEntries_AppId_ExpiresAt",
                table: "HandshakeReplayEntries",
                columns: new[] { "AppId", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_HandshakeReplayEntries_NonceHash",
                table: "HandshakeReplayEntries",
                column: "NonceHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SessionSaltStates_RotatedAt",
                table: "SessionSaltStates",
                column: "RotatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SessionSaltStates_SessionId_IsActive",
                table: "SessionSaltStates",
                columns: new[] { "SessionId", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HandshakeReplayEntries");

            migrationBuilder.DropTable(
                name: "SessionSaltStates");
        }
    }
}
