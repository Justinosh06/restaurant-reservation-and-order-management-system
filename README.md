# Restaurant Reservation and Order Management System

[![Version 1.0.2](https://img.shields.io/badge/version-1.0.1-blue)](https://github.com/Justinosh06/restaurant-reservation-and-order-management-system/releases)

A full-stack restaurant management application built with ASP.NET Core Razor Pages, Entity Framework Core, and SQL Server. The system supports customer-facing reservation and ordering workflows, staff/admin operations, Stripe-integrated deposits and payments, and a complete restaurant administration dashboard for managing tables, menus, promotions, announcements, and reporting.

## Overview

This project is designed for a modern restaurant business that needs to manage:

- table bookings and guest reservations
- customer account registration and setup
- menu browsing and ordering
- online payment and deposit handling
- dining sessions and order tracking
- staff/admin management and permissions
- promotions and announcements
- review collection and policy display
- operational reporting and analytics

The application is split between a public customer-facing site and an admin portal that runs on a separate host (`admin.localhost`), allowing clearer role separation between guests/customers and restaurant staff.

## Key Features

### Customer Experience

- Customer registration and login
- Profile setup and validation before access to protected pages
- Reservation booking with date, time slot, table capacity, and notes
- Deposit payments using Stripe Checkout for online bookings
- Menu browsing by category and item type
- Add-to-cart ordering workflow for dine-in sessions
- Dining session tracking and order status monitoring
- Promotions and discount codes
- Review submission and display
- Policy and privacy pages
- Notification center for announcements and updates

### Staff and Admin Operations

- Role-based access control for administrators and staff
- Admin sign-in portal with separate host routing
- Dashboard overview for restaurant insights
- Reservation management and status tracking
- Table assignment and occupancy control
- Menu management and dish availability toggling
- Order monitoring and kitchen status flow
- Announcement publishing and customer notifications
- Promotion management and email-triggered promotion workflows
- Customer and staff oversight
- Restaurant settings and business-hours configuration
- Analytics reporting with PDF/CSV export support

### Business and Integration Features

- SQL Server data persistence via Entity Framework Core
- Stripe payment integration for deposits and checkout workflows
- SMTP-based email notifications (configuration-ready for local Mailpit or production SMTP)
- CAPTCHA support for login and registration flows
- PDF export capability for analytics/reporting using QuestPDF
- Custom routing for admin portal access on a separate subdomain

## Technology Stack

- ASP.NET Core 10 Razor Pages
- C# / .NET
- Entity Framework Core
- Microsoft SQL Server
- Stripe .NET SDK
- QuestPDF
- Markdig
- Cookie-based authentication and role-based authorization

## Project Structure

```text
.
├── Data/
│   ├── ApplicationDbContext.cs
│   ├── DbSeeder.cs
│   ├── MenuItemSeeder.cs
│   ├── TableSeeder.cs
│   └── UtcDateTimeConverter.cs
├── Enums/
├── Extensions/
├── Infrastructure/
│   ├── Admin/
│   ├── Announcements/
│   ├── Authentication/
│   ├── Email/
│   ├── Navigation/
│   ├── Payments/
│   ├── Reservations/
│   ├── Reviews/
│   ├── Routing/
│   └── Stripe/
├── Migrations/
├── Models/
│   ├── Admin.cs
│   ├── Announcement.cs
│   ├── CartItem.cs
│   ├── Customer.cs
│   ├── Feedback.cs
│   ├── MenuItem.cs
│   ├── Order.cs
│   ├── OrderItem.cs
│   ├── Promotion.cs
│   ├── Reservation.cs
│   ├── ReservationTable.cs
│   ├── RestaurantSettings.cs
│   ├── Session.cs
│   ├── Table.cs
│   ├── Transaction.cs
│   └── ...
├── Pages/
│   ├── Admin/
│   ├── Customer/
│   ├── Menu/
│   ├── Payment/
│   ├── Policies/
│   ├── Reservation/
│   ├── Shared/
│   ├── Table/
│   ├── Index.cshtml
│   ├── Login.cshtml
│   ├── Register.cshtml
│   └── ...
├── Services/
├── wwwroot/
├── Program.cs
├── appsettings.json
├── appsettings.Development.json
├── Restaurant Reservation and Order Management System.csproj
├── .gitignore
└── README.md
```

## Main Domain Models

The application uses relational models that represent the business domain:

- Admin
- Customer
- Table
- Reservation
- ReservationTable
- Session
- CartItem
- Order
- OrderItem
- MenuItem
- Transaction
- Feedback
- RestaurantSettings
- Announcement
- Promotion

These entities are defined in the `Models` folder and mapped through `ApplicationDbContext` in `Data/ApplicationDbContext.cs`.

## Application Flow

### Public Site

Visitors can:

1. browse the restaurant homepage
2. access the menu
3. register and sign in
4. create a reservation
5. complete profile setup if required
6. place orders during a dining session
7. pay deposits or settle payments through Stripe
8. leave feedback and view promotions

### Admin Portal

Admins and staff can:

1. sign in through the admin host
2. access a dashboard with business analytics
3. manage reservations, tables, and dining sessions
4. update menu availability and menu content
5. manage customer notifications, promotions, and announcements
6. monitor orders and operational trends

## Setup and Installation

### Prerequisites

- .NET SDK 10.0
- SQL Server instance running locally or in a dev environment
- Access to a web browser for the application UI
- Optional: Stripe account for payment testing
- Optional: Mailpit or an SMTP server for email delivery testing

### Clone the Repository

```bash
git clone https://github.com/Justinosh06/restaurant-reservation-and-order-management-system.git
cd "Restaurant Reservation and Order Management System"
```

### Configure the Database

The project uses the `DefaultConnection` connection string in `appsettings.json` and `appsettings.Development.json`.

By default, the app expects a SQL Server instance similar to:

```json
"ConnectionStrings": {
  "DefaultConnection": "Data Source=localhost,1435;Initial Catalog=RestaurantDb;User ID=SA;Password=YourStrongPassword;Pooling=False;Trust Server Certificate=True;Authentication=SqlPassword"
}
```

Update this value to match your local SQL Server setup.

### Restore and Build

```bash
dotnet restore
dotnet build
```

### Run the Application

```bash
dotnet run
```

Then open one of the following URLs in the browser:

- Main storefront: `https://localhost:<port>/`
- Admin portal: `https://admin.localhost:<port>/`

If your local machine does not resolve `admin.localhost`, add the following entry to your hosts file:

```text
127.0.0.1 localhost admin.localhost
```

On Linux or macOS, this is typically in `/etc/hosts`.
On Windows, use the host file under `C:\Windows\System32\drivers\etc\hosts`.

## Seeded Default Accounts

The app auto-seeds default admin accounts when the database is created or migrated.

- Administrator: `admin@restaurant.com`
- Password: `Admin@123`
- Staff: `staff@restaurant.com`
- Password: `Staff@123`

These credentials are intended for development and demonstration use.

## Payment and Email Configuration

### Stripe

The app reads Stripe configuration from the `Stripe` section of the app settings file. Update the values to your own test or production keys before using live payment flows.

For table orders, customers use one checkout button after selecting Stripe or cash at the counter. Cash requests remain pending until an administrator records the payment from the Admin Orders page. The administrator enters the amount received and selects an existing customer email or enters a new one; new email addresses are registered automatically, and any change due is calculated and shown. After confirmation, the customer's checkout page updates order states live and redirects to the payment status page, where a QuestPDF receipt can be downloaded. Administrators can also download the receipt from order history.

### Email

Email settings are configured under the `Email` section in configuration. For local development, Mailpit is a common option.

Typical local development setup:

- SMTP host: `localhost`
- SMTP port: `1025`
- Mailpit inbox: `http://localhost:8025`

## Database and Migration Notes

The application uses EF Core migrations and also contains migration-safe seeding logic in `Data/DbSeeder.cs`.

When the app starts, it will:

- migrate pending database changes if needed
- ensure the database exists
- seed default admin accounts
- seed tables and menu items
- initialize application data required for a working demo

## Admin and Role Model

The app implements role-based policies:

- `AdminPortal` for admin/staff access
- `AdministratorOnly` for full admin permissions
- `CustomerOnly` for customer access

This separation helps keep staff workflows and customer flows distinct while preserving a unified application.

## Notes for Developers

- Razor Pages are used for both the customer portal and admin portal.
- The app uses custom route logic to keep admin pages under the `admin` subdomain.
- The system is designed to support both dine-in and reservation-driven operations.
- Admin and customer profiles are explicitly validated for required information before access is granted.
- The project includes both business analytics and operational tools for restaurant management.

## Recommended Development Workflow

1. Start SQL Server and confirm the database connection string works.
2. Run `dotnet restore`.
3. Run `dotnet build`.
4. Start the app with `dotnet run`.
5. Sign in to the admin portal using the seeded admin account.
6. Configure restaurant settings, menu items, tables, and promotion data.
7. Test the customer flow for reservations, order placement, and payment.

## Project Status

This README describes Version 1.0 of the project. It reflects the current application architecture and initial feature set as implemented in the codebase.

## Disclaimer

This project is intended for educational, demonstration, and practical restaurant-management use. For production deployment, additional hardening is recommended, including:

- secure secret management
- production-grade SMTP/email setup
- HTTPS certificate management
- environment-specific configuration
- further security review and penetration testing
- backup and recovery strategy

## License

No explicit license file was provided in the repository. Please confirm licensing terms with the project owner before commercial reuse or redistribution.
