using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinFlow.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVendors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE uploaded_document_draft
                ADD COLUMN IF NOT EXISTS image_content_type character varying(100);
                """);

            migrationBuilder.Sql("""
                ALTER TABLE uploaded_document_draft
                ADD COLUMN IF NOT EXISTS image_data bytea;
                """);

            migrationBuilder.AlterColumn<DateTime>(
                name: "period_start",
                table: "tenant_subscription",
                type: "date",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<DateTime>(
                name: "period_end",
                table: "tenant_subscription",
                type: "date",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.Sql("""
                ALTER TABLE tenant_membership
                ADD COLUMN IF NOT EXISTS "DeactivatedAt" timestamp with time zone;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE tenant_membership
                ADD COLUMN IF NOT EXISTS "DeactivatedBy" uuid;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE tenant_membership
                ADD COLUMN IF NOT EXISTS "DeactivatedReason" text;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE tenant_membership
                ADD COLUMN IF NOT EXISTS department_id uuid;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE invitation
                ADD COLUMN IF NOT EXISTS "DepartmentId" uuid;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE invitation
                ADD COLUMN IF NOT EXISTS "RevokedByMembershipId" uuid;
                """);

            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS tenant_usage_snapshot (
                    "Id" uuid NOT NULL,
                    id_tenant uuid NOT NULL,
                    period_start date NOT NULL,
                    period_end date NOT NULL,
                    ocr_pages_used integer NOT NULL DEFAULT 0,
                    chatbot_messages_used integer NOT NULL DEFAULT 0,
                    storage_used_bytes bigint NOT NULL DEFAULT 0,
                    is_active boolean NOT NULL DEFAULT TRUE,
                    CONSTRAINT "PK_tenant_usage_snapshot" PRIMARY KEY ("Id"),
                    CONSTRAINT "FK_tenant_usage_snapshot_tenant_id_tenant"
                        FOREIGN KEY (id_tenant) REFERENCES tenant ("Id") ON DELETE RESTRICT
                );
                """);

            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS vendor (
                    "Id" uuid NOT NULL,
                    id_tenant uuid NOT NULL,
                    tax_code character varying(14) NOT NULL,
                    name character varying(200) NOT NULL,
                    is_verified boolean NOT NULL DEFAULT FALSE,
                    verified_by_membership_id uuid NULL,
                    verified_at timestamp with time zone NULL,
                    created_at timestamp with time zone NOT NULL,
                    updated_at timestamp with time zone NOT NULL,
                    is_active boolean NOT NULL DEFAULT TRUE,
                    CONSTRAINT "PK_vendor" PRIMARY KEY ("Id")
                );
                """);

            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX IF NOT EXISTS ux_tenant_usage_snapshot_period
                ON tenant_usage_snapshot (id_tenant, period_start, period_end);
                """);

            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_vendor_id_tenant_tax_code"
                ON vendor (id_tenant, tax_code);
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_vendor_is_verified"
                ON vendor (is_verified);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS ux_tenant_usage_snapshot_period;
                """);

            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS "IX_vendor_id_tenant_tax_code";
                """);

            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS "IX_vendor_is_verified";
                """);

            migrationBuilder.Sql("""
                DROP TABLE IF EXISTS tenant_usage_snapshot;
                """);

            migrationBuilder.Sql("""
                DROP TABLE IF EXISTS vendor;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE uploaded_document_draft
                DROP COLUMN IF EXISTS image_content_type;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE uploaded_document_draft
                DROP COLUMN IF EXISTS image_data;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE tenant_membership
                DROP COLUMN IF EXISTS "DeactivatedAt";
                """);

            migrationBuilder.Sql("""
                ALTER TABLE tenant_membership
                DROP COLUMN IF EXISTS "DeactivatedBy";
                """);

            migrationBuilder.Sql("""
                ALTER TABLE tenant_membership
                DROP COLUMN IF EXISTS "DeactivatedReason";
                """);

            migrationBuilder.Sql("""
                ALTER TABLE tenant_membership
                DROP COLUMN IF EXISTS department_id;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE invitation
                DROP COLUMN IF EXISTS "DepartmentId";
                """);

            migrationBuilder.Sql("""
                ALTER TABLE invitation
                DROP COLUMN IF EXISTS "RevokedByMembershipId";
                """);

            migrationBuilder.AlterColumn<DateTime>(
                name: "period_start",
                table: "tenant_subscription",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "date");

            migrationBuilder.AlterColumn<DateTime>(
                name: "period_end",
                table: "tenant_subscription",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "date");
        }
    }
}
