#!/bin/bash

dotnet run --project mk-keys.csproj -- make-keys "relkeys"
dotnet run --project mk-keys.csproj -- sign-keys "relkeys.pub" ../../src/dnvm/Resources/root.pem