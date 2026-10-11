using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DruzhokBot.Common.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialChatSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "chat_settings",
                columns: table => new
                {
                    ChatId = table.Column<long>(type: "INTEGER", nullable: false),
                    GreetingEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    GreetingText = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chat_settings", x => x.ChatId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chat_settings");
        }
    }
}
