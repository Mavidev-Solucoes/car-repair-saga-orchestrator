using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

public partial class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "saga_instances",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CorrelationId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                CurrentState = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_saga_instances", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "saga_history",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                SagaInstanceId = table.Column<Guid>(type: "uuid", nullable: false),
                FromState = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                ToState = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                Trigger = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Details = table.Column<string>(type: "text", nullable: true),
                OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_saga_history", x => x.Id);
                table.ForeignKey(
                    name: "FK_saga_history_saga_instances_SagaInstanceId",
                    column: x => x.SagaInstanceId,
                    principalTable: "saga_instances",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_saga_history_SagaInstanceId_OccurredAtUtc",
            table: "saga_history",
            columns: new[] { "SagaInstanceId", "OccurredAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_saga_instances_CorrelationId",
            table: "saga_instances",
            column: "CorrelationId",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "saga_history");

        migrationBuilder.DropTable(
            name: "saga_instances");
    }
}
