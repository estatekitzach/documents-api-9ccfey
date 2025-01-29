```
Input: EstateKit - Third Party Passwords API (business logic and data access)
WHY - Vision & Purpose
Purpose & Users
The Estate Kit application topology needs an API that stores documents within AWS S3 securely, methods to delete or update those documents, and has methods to manage the archiving of old documents. 
This API will also provide methods to pass documents to it, which then are analyzed by document analysis software, primarily AWS Textract, to provide the results of the data contained in the document. The results will return:
name/value pairs of data
Csv contents of data tables
Text found within the document
The primary users of this API will be Estate Kit applications and other web services. 
What - Core Requirements	
File upload
	The API will provide a method to upload files for a specific user. The calling system will pass data in the following format:


The flow will be:
The process will authenticate the call by confirming the authentication token is valid by checking it with Cognito OAuth service. 
The process will receive the file being uploaded, converting it from a byte array to an object.  
The process will check that the file extension is a valid for the data type being passed
Image files - png, jpeg, gif, jpg
Document files - .doc, .docx, .xls, .xlsx, .csv, .pdf
The process will authenticate against AWS S3. 
The process will combine the user id, document type, and then encrypt the value into a document path key to be used for the user. Document types are: 
Password files
Medical 
Insurance
Personal identifiers 
The process will check if the document path key exists within S3. 
If the document key path does not exist, the process will create the folder within S3.
The process will then create a new file name value by encrypting the current file name, but without the file extension. 
The system will save the file within the document key path, using the newly encrypted file name. 
The system will return a success message consisting of the user id, the new file name for the document, and the document key path value. 
	

File Deletion
	There will be times that the system needs to delete files. This flow will be:
The request will send the document to be deleted in the documented data format for the API
The system will authenticate the request against Cognito. 
The system will confirm the file exists within S3 and not in use
The system will delete the file from S3
The system will respond indicating the file was successfully deleted

	
	File OCR AI Processing
	The system will provide functionality to analyze a document for text using Optical Character Recognition (OCR), analyze the data, and return the contents of the document with the text, any name/value pairs and any table data in comma separated values. The flow will be: 
The request will send the document to be deleted in the documented data format for the API
The system will authenticate the request against Cognito.
The data will contain either a document path or a byte array consisting of the document. 
The system will authenticate to AWS Textract. 
Using AWS’s SDK, the system will upload the document to TextRact for processing. 
The system will package the results from TextRact as a JSON result and return it to the calling process. 


HOW - Planning & Implementation
Technical Foundation
Required Stack Components
Backend: REST API service. Use .net Core 9 with C#
Storage: AWS S3 bucket
OCR Engine: AWS Textract
Event Logging - Cloudwatch
Container orchestration: AWS EKS
PaaS: AWS
Authentication: AWS Cognito (OAuth)

System Requirements
Performance: load time under 3 seconds
Security: End-to-end encryption, secure authentication, financial regulatory compliance. 
Reliability: 99.9% uptime, daily data synchronization, automated backups
Testing: Comprehensive unit testing, security testing
Data format - All calls to the document API will send the document data in the following structure:

Data:
User_id: integer required,
	Document_name: string ,
	Document: array of bytes,
	Document_type: number,
	Document_type_name: string,
	Document_url: string nullable

JSON:
userDocument{
	User_id: ###,
	Document_name: “”,
	Document: [],
	Document_type: ##,
	Document_type_name: “”
	Document_url : “”
}
Business Requirements
All calls to the business logic or data APIs must contain valid security tokens from the OAuth provider. 
There should be no calls allowed that are not using OAuth authentication 
The system will house its credentials for AWS S3 and Textract from environment variables. 





```