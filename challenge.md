# Backend Developer Challenge

## Introduction

This technical task is designed to see how you approach a complex problem. We are
looking to understand how you break down your solution, what you consider when you
are making your decisions, and how you write and structure code.

We are not necessarily looking for a fully working or bug-free solution. After you
have submitted, you will have the opportunity to talk through your approach, what
you found challenging, and why you made the decisions you did.

We think that a reasonable solution for discussion could be produced within a few
hours, but feel free to spend as long or as short as you like on this.

## Brief

Create a hotel room booking API using ASP.NET Core and Entity Framework (EF)
Core. Your solution must be written in C# following RESTful principles.

The solution should be committed to an online repository and access shared with
us. If you have any supporting documentation, please include this in the
repository.

If possible, it should be hosted in an Azure environment (free trials are
available), please note this is not a critical requirement.

Use the database of your choosing.

## Business Rules

- Hotels have 3 room types: single, double, deluxe.
- Hotels have 6 rooms.
- A room cannot be double booked for any given night.
- Any booking at the hotel must not require guests to change rooms at any point
  during their stay.
- Booking numbers should be unique. There should not be overlapping at any
  given time.
- A room cannot be occupied by more people than its capacity.

## Requirements

The system should provide the following functionality through a well-designed API.

### Business Functionality

Your solution must allow an API consumer to perform the following:

- Find a hotel based on its name.
- Find available rooms between two dates for a given number of people.
- Book a room.
- Find booking details based on a booking reference number.

### Technical Requirements

- The API must be testable.
  - OpenAPI / Swagger documentation should be made available for testing.
  - For testing purposes, the API should expose functionality to allow for
    seeding and resetting the data:
    - Seeding: Populate database with just enough data for testing.
    - Resetting: Remove all data ready for seeding.
  - Consideration could be given to automated testing but is not essential to
    the deliverable.
- The API requires no authentication.
