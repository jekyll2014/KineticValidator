# KineticValidator

Validation of a complex JSON-based applications of the KINETIC framework.
The problem is low-code platform does not have dedicated IDE providing code completion and design-time code check. The framework core doesn not check/warn about the issues as well. But sthill developer has to spend lots of time finding naming errors and tracing method call manually through the JSON text.
The idea is to find cases not coverable by schema like specific properties values inconsistence over all the files included into the application.
The second idea is to show the errors found in a convenient editor form to reduce time developer spend for findinf/opening file and looking for the lines/paths in the file.

Exaples:
1) looking for executable methods names and comparing it against the list of the methods called to find undefined methods (incorrect names due to a copy/paste?)
2) Looking for a REST methods called in the application and comparing it with contracts defined in the server DLL's to see incorrect REST method names as well as parameters.
3) Looking for entity override (entities with the same name declared in different places).
4) Looking for redundant entities (to be removed or not used due to naming errors)
5) Missing forms (incorrect names?)

There are totally 48 cases to be detected. All cases are the result of my own and tech. support group experience.
