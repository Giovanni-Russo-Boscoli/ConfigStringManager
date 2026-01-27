
# Config String Manager 
A tool for inspecting, editing, and managing connection strings across multiple BOMi configuration files.

## Overview
Config String Manager is a desktop utility designed to simplify the process of locating, reading, and updating connection strings inside various config files used across BOMi. It provides a clean interface for browsing configuration files, selecting connection entries, editing server/database values, and saving changes.

This tool is especially useful for developers, DevOps engineers, and support teams who frequently switch environments or maintain multiple application instances.

### Setup required:

Run the application for the first time and it will create in your desktop, a folder called ConfigStringManagerSetup. 

It will contains two files:

1. ***bomiEnvironments.json***: You must edit this file replacing the placeholders with your actual paths. 

    See sample below:

    ```
    [
        {
        "Name": "INTEG MIRROR",
        "PrefixPath": "D:\\YOUR_ENVIRONMENT_PATH\\Trunk\\",
        "STSPrefixPath": "D:\\YOUR_STS_PATH\\Sdm.App.STS\\"
        }
    ]

        - Name: Environment name (Any string without special characters)
        - PrefixPath: Path to the trunk folder for BOMi repo
        - STSPrefixPath: Path to the STS folder (You should use the first "Sdm.App.STS" folder, not the inner one)
    ```
    You can add multiple environments:
    ```
    [
        {
            "Name": "ENVIRONMENT 1",
            "PrefixPath": "D:\\YOUR_ENVIRONMENT_PATH\\Trunk\\",
            "STSPrefixPath": "D:\\YOUR_STS_PATH\\Sdm.App.STS\\"
        },
        {
            "Name": "ENVIRONMENT 2",
            "PrefixPath": "D:\\YOUR_ENVIRONMENT_PATH\\Trunk\\",
            "STSPrefixPath": "D:\\YOUR_STS_PATH\\Sdm.App.STS\\"
        },
        {
            "Name": "ENVIRONMENT 3",
            "PrefixPath": "D:\\YOUR_ENVIRONMENT_PATH\\Trunk\\",
            "STSPrefixPath": "D:\\YOUR_STS_PATH\\Sdm.App.STS\\"
        }
    ]
    ```
2. ***servers.json***:
            - It gives you an extense list of servers available. 
            You can edit it by removing or adding servers as you wish. 
            If you decide to revert your changes you can simply delete the file and it will recreate the file with the default list of servers
            when you run the application.

### Key Features:

* Displays files in a structured TreeView for quick navigation.

* Shows connection strings extracted from the following files:

        - SdmApp.WebConfig
        - SdmApp.Web-bomi2AppSettings.config
        - SdmApp.PubSub.WebConfig
        - SdmApp.PubSub.Web-bomi2AppSettings.config
        - SdmApp.MonitoringConfiguration (Profiling)
        - Sdm.Log4Net
        - STS.WebConfig
        
* The files highlighted above can be expanded to see all the connection strings in it, and update them individually.

* Editing Connection Strings       
        
     There are few ways you can edit the connection strings:

    - By ***Environment***: You click on the Environment name and the right panel will be available to select a server. 
    
        This feature updates only the servers, for all the files/connection strings of that environment.

    - By ***File***: You click on the file name and the right panel will be available to select a server.
    
        This feature updates only the servers, for all connection strings of that file.

    - By ***Connection String***: You click on the connection string name, it will populate Server and Database combobox with the values coming from the connection string you clicked on. Select a **server** and **database** and save it.

        -  Server
            - If the server is unavailable, the dropdown still shows the server parsed from the file, plus all the servers from the "servers.json" file.
            - Servers can NOT be updated manually, it has to be selected from the dropdown
        - Database
            - If the database is unavailable, the dropdown still shows the database parsed from the file.
            - It can be updated manually
    
* Whitespace and formatting are preserved. (Prevent unnecessary changes - Use regex expression instead XDocument to avoid normalizing files)
    
* Reload Files Button:
    - A dedicated **Reload Files** button allows users to refresh all configuration files without restarting the application. 
        (useful when files are edited externally).

* Support multiple BOMi environments (usefull when you are working on multiple projects/tasks simultaneously)


