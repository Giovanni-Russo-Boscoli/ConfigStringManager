-  Config String Manager 
    - A tool for inspecting, editing, and managing connection strings across multiple BOMi configuration files.

- Overview
    - Config String Manager is a desktop utility designed to simplify the process of locating, reading, and updating connection strings inside various 
    .config files used across BOMi. It provides a clean interface for browsing configuration files, selecting connection entries, editing server/database 
    values, and saving changes.

    - This tool is especially useful for developers, DevOps engineers, and support teams who frequently switch environments or maintain multiple 
    application instances.

- Setup required:
    - Run the application for the first time and it creates

- Key Features

    - Displays files in a structured TreeView for quick navigation.

    - Shows connection strings extracted from the following files:
        - SdmApp.WebConfig
        - SdmApp.Web-bomi2AppSettings.config
        - SdmApp.PubSub.WebConfig
        - SdmApp.PubSub.Web-bomi2AppSettings.config
        - SdmApp.MonitoringConfiguration (Profiling)
        - Sdm.Log4Net
        - STS.WebConfig

    - Editing Connection Strings (You can modify)        
        -  Server
            - If the server is unavailable/unreachable, the dropdown still shows the server parsed from the file.
            - Server dropdown is loaded with a list of servers from "servers.json" file found in ConfigStringManager folder, created in your desktop.
            (The folder is created when the application is ran for the first time)
            - You can edit the file adding or removing servers as needed.
            - Servers can NOT be updated manually
        - Database
            - If the database is unavailable/unrecheable, the dropdown still shows the database parsed from the file.
            - Can be updated manually
    
    - Whitespace and formatting are preserved. (Prevent unnecessary changes - Use regex expression instead XDocument to avoid normalizing files)
    
    - The server dropdown lists all known servers from your configuration. ??????????????????

    - Reload Files Button
        - A dedicated Reload Files button allows users to refresh all configuration files without restarting the application. 
        (useful when files are edited externally).

    - Support multiple BOMi environments (usefull when you are working on multiple projects/tasks simultaneously)


