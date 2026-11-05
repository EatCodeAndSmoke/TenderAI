# Amazon Bedrock tender summaries and matching

The API defaults to Amazon Nova Micro's EU geographic inference profile:

- Region: `eu-central-1` (Frankfurt)
- Model ID: `eu.amazon.nova-micro-v1:0`

Nova Micro has Geo inference, but not in-Region inference, in Frankfurt. The EU profile routes only within the EU geography. Its Nova Micro destination Regions are Frankfurt (`eu-central-1`), Stockholm (`eu-north-1`), Ireland (`eu-west-1`), and Paris (`eu-west-3`). The profile ID is deliberately separate from the underlying foundation-model ID: application requests use `eu.amazon.nova-micro-v1:0`, while IAM permissions must include the `amazon.nova-micro-v1:0` foundation model in each destination Region.

## Tender matching

Selecting **Find tenders for me** in Business Banking evaluates the signed-in customer against the list of currently open UZEX tenders. The model receives the customer ID, OKED code/activity, location, up to 30 incoming-payment descriptions from the preceding 12 months, and each open tender's ID, location, description, and product details. Customer names, TINs, balances, pre-approved limits, and overdue data are not sent to Bedrock. The model is instructed to identify what the buyer is procuring and what the customer sells, then match only when both OKED activity and incoming-payment evidence support selling goods or services in the same specific category. A related industry, material used, or shared keyword is not sufficient; location is a preference, not a requirement. Incomplete or ambiguous evidence should be excluded.

Business Banking searches for up to three new matches by default, evaluating open, unchecked stored tenders first in batches of up to five and importing more from UZEX as needed. Once a new match is saved, the search stops when 10 seconds have elapsed since the search began, unless the target or another natural limit is reached first. If the first match arrives after 10 seconds, it is saved before the search stops. With no new matches, the search continues until it exhausts available tenders or reaches the import limit. The limit does not include matches saved by previous searches. While searching, the UI shows the number of tenders evaluated and new matches found; checked counts include all tenders in an evaluated batch, even if the target or time limit is reached before every result is saved. Processed tenders are recorded per customer, so subsequent searches continue with unprocessed tenders rather than repeating earlier results; other customers can still be evaluated against them. Different lot IDs with the same normalized buyer, product, and key source terms are shown once per customer. Opening and closing dates must match, but their times may differ; prices, quantities, descriptions and other recorded terms must match. Linked technical documents are not parsed or compared, so grouping cannot account for differences inside them. Disliking a displayed duplicate hides all copies; existing feedback and applications remain in the database. An open saved copy is shown ahead of a closed copy. Saved matches remain visible across searches and visits until disliked or deleted. Expired matches remain visible as closed and cannot be applied for. Fewer new matches are returned if fewer are found. A search imports at most 50 new tenders; if that limit is reached, the customer can search again. API callers may still request 1–10 matches explicitly. Likes and dislikes persist per customer, and disliked tenders are excluded from later searches. Editing a customer or changing the matching prompt allows previously evaluated nonmatches to be checked again without rediscovering saved matches. Deleting an individual tender permanently removes its product, matches, discovery history, feedback and applications for all customers; it can be imported again. Previously soft-deleted lots are eligible for reimport, at which point their old records are replaced. The admin's **Clear all tenders** action permanently removes every tender and related customer record.

New matches appear at the top as they stream in. Customers can mark a matched tender as **Applied**; this records a request for preparation in the demo database and does not submit an application to UZEX or trigger a real preparation process. Application records are separate from likes and dislikes, survive later searches, and are removed if the associated customer or tender is deleted.

Each customer evaluation is a billable model invocation, with bounded retries for throttling. Results are saved per customer and streamed to Business Banking; a failed evaluation preserves the previous results. Loan-application invitations are calculated locally: the tender must be in UZS, a positive pre-approved limit must cover at least 50% of the tender's starting price, and both current overdue amount and maximum overdue days in the last 12 months must be zero. Saved matches are evaluated against the current loan rules and customer risk data when retrieved, without changing their matching history. Suggested amounts do not exceed the limit and are not loan approvals. Loan requests are previews only.

The admin **Prompts** page shows the current matching and summarization instructions and allows each to be edited or restored to its default. Saved instructions persist in SQLite and apply to subsequent model requests. The API always appends the structured source data, mandatory activity/payment matching criteria and required response formats; matching output validation and the local loan rules remain in effect. Existing custom prompts are not overwritten on deployment: select **Restore default** and save to use the latest recommended matching instructions. Existing matches and summaries are not automatically regenerated when instructions change; customers can dislike an incorrect match. AI judgments are not guaranteed and should be reviewed. The demo admin API is not authenticated; restrict access to the application in any deployment.

New databases do not receive fictional customer or notification seed data automatically. The admin Customers page can generate one random fictional business with local mock activity, payments, and risk data, or add one manually. Generation chooses among the least-used mock OKED activities to provide a mix of industries; each activity has corresponding payment descriptions. **Details** opens an editable customer form, including its risk and payment information. Generation does not require UZEX or Bedrock and does not guarantee any particular matching or financing result. Each customer receives a fictional multicurrency account with GEL, USD and EUR balances. The card and loan request are illustrative, and notifications are inactive. Startup removes the exact legacy sample profiles and old notifications that have no matching explanation.

This is a hackathon prototype: the shared demo password and customer-ID-based API are not secure authentication and must not be used for real banking data.

## EC2 role permissions

Attach a policy like the following to the EC2 instance-profile role used by the API container. Replace `<ACCOUNT_ID>` with the AWS account ID. It grants only non-streaming model invocation, which is the operation this API uses:

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Sid": "InvokeNovaMicroEuProfile",
      "Effect": "Allow",
      "Action": "bedrock:InvokeModel",
      "Resource": [
        "arn:aws:bedrock:eu-central-1:<ACCOUNT_ID>:inference-profile/eu.amazon.nova-micro-v1:0",
        "arn:aws:bedrock:eu-central-1::foundation-model/amazon.nova-micro-v1:0",
        "arn:aws:bedrock:eu-north-1::foundation-model/amazon.nova-micro-v1:0",
        "arn:aws:bedrock:eu-west-1::foundation-model/amazon.nova-micro-v1:0",
        "arn:aws:bedrock:eu-west-3::foundation-model/amazon.nova-micro-v1:0"
      ]
    }
  ]
}
```

AWS requires permission on both the inference-profile resource and the foundation-model resource in every Region that profile can route to. If the application later uses streaming inference, add `bedrock:InvokeModelWithResponseStream` as well. This service does not create, list, or manage inference profiles, so profile-management permissions are not needed.

## Switching to Nova Pro

The existing matching and summarization services use the same configured model. Nova Pro uses the same Nova 1 Invoke API, so switching models does not require changing either service:

1. In the AWS EC2 console, select the API's instance and open its **Security** tab to find the attached IAM role. In IAM, add the following policy to that role, replacing `<ACCOUNT_ID>` with your AWS account ID. Keep the Micro policy if you want to be able to switch back.
2. In GitHub, open the repository's **Settings → Secrets and variables → Actions → Variables** and create or update the repository variable `BEDROCK_MODEL_ID` to `eu.amazon.nova-pro-v1:0`. Set `BEDROCK_REGION` to `eu-central-1` if it is not already set to that value.
3. In **Actions → Deploy TenderAI**, select **Run workflow**, choose `main`, and run it. The self-hosted runner must be online. Changing a variable alone does not restart the API.
4. After the deployment succeeds, try a new search and a newly imported tender. Check the API logs for Bedrock authorization or configuration errors if matching or summarization fails.

```json
{
  "Version": "2012-10-17",
  "Statement": [{
    "Effect": "Allow",
    "Action": "bedrock:InvokeModel",
    "Resource": [
      "arn:aws:bedrock:eu-central-1:<ACCOUNT_ID>:inference-profile/eu.amazon.nova-pro-v1:0",
      "arn:aws:bedrock:eu-central-1::foundation-model/amazon.nova-pro-v1:0",
      "arn:aws:bedrock:eu-north-1::foundation-model/amazon.nova-pro-v1:0",
      "arn:aws:bedrock:eu-west-1::foundation-model/amazon.nova-pro-v1:0",
      "arn:aws:bedrock:eu-west-3::foundation-model/amazon.nova-pro-v1:0"
    ]
  }]
}
```

This EU profile can route requests among Frankfurt, Stockholm, Ireland, and Paris. Check for organization-level restrictions or account-level model access if invocation is denied. Nova Pro is billed at different rates from Micro; check the current [Bedrock pricing](https://aws.amazon.com/bedrock/pricing/) for your Region. Switching models does not regenerate saved summaries or matches or clear evaluated-tender history. If a full reset is intended, **Clear all tenders** removes those records, including likes and applications, and allows lots to be imported and evaluated again. Review new results: a stronger model is not a guarantee of correct matching or summaries.

## Configuration

The default values are defined in `backend/TenderAI.Api/appsettings.json`. Docker Compose accepts optional `BEDROCK_REGION` and `BEDROCK_MODEL_ID` environment variables; the deployment workflow reads the same-named GitHub Actions repository variables. Set both variables if selecting a different source Region or inference profile. Keep AWS credentials out of configuration: the AWS SDK uses the EC2 instance role/default credential chain.

The AWS Console notice requesting use-case details is for Anthropic model onboarding, not Amazon Nova. Nova Micro does not require that Anthropic-specific form or an uploaded use-case document.

Bedrock model invocation is billable. Actual availability and account-level restrictions should be checked in the AWS account before deployment.

## References

- [Nova Micro model card and regional availability](https://docs.aws.amazon.com/bedrock/latest/userguide/model-card-amazon-nova-micro.html)
- [Prerequisites for inference profiles and IAM policy requirements](https://docs.aws.amazon.com/bedrock/latest/userguide/inference-profiles-prereq.html)
