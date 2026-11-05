# Amazon Bedrock tender summaries and matching

The API defaults to Amazon Nova Micro's EU geographic inference profile:

- Region: `eu-central-1` (Frankfurt)
- Model ID: `eu.amazon.nova-micro-v1:0`

Nova Micro has Geo inference, but not in-Region inference, in Frankfurt. The EU profile routes only within the EU geography. Its Nova Micro destination Regions are Frankfurt (`eu-central-1`), Stockholm (`eu-north-1`), Ireland (`eu-west-1`), and Paris (`eu-west-3`). The profile ID is deliberately separate from the underlying foundation-model ID: application requests use `eu.amazon.nova-micro-v1:0`, while IAM permissions must include the `amazon.nova-micro-v1:0` foundation model in each destination Region.

## Tender matching

Selecting **Find tenders for me** in Business Banking evaluates the signed-in customer against the list of currently open UZEX tenders. The model receives the customer ID, OKED code/activity, location, up to 30 incoming-payment descriptions from the preceding 12 months, and each open tender's ID, location, description, and product details. Customer names, TINs, balances, pre-approved limits, and overdue data are not sent to Bedrock. The model is instructed to return a match only when both the OKED activity and payment evidence support the tender product; location is a preference, not a requirement, and incomplete or ambiguous evidence should be excluded.

Business Banking searches for up to three new matches by default, streaming matching stored tenders first and importing more from UZEX as needed. While searching, the UI shows the number of tenders evaluated and new matches found; checked counts include all tenders in an evaluated batch, even if the target is reached before every result is saved. Evaluated tenders are recorded per customer, so subsequent searches continue with unseen tenders rather than repeating earlier results; other customers can still be evaluated against them. Different lot IDs with the same normalized buyer, product, and key source terms are shown once per customer. Matching is conservative: differently described lots or different prices, quantities, deadlines or other terms are kept separate. Disliking a displayed duplicate hides all copies; existing feedback and applications remain in the database. Saved matches remain visible across searches and visits until disliked or deleted. Expired matches remain visible as closed and cannot be applied for. Fewer new matches are returned if fewer are found. A search checks at most 50 newly imported tenders; if that limit is reached, the customer can search again. API callers may still request 1–10 matches explicitly. Likes and dislikes persist per customer, and disliked tenders are excluded from later searches. Editing a customer or changing the matching prompt allows previously evaluated nonmatches to be checked again without rediscovering saved matches. Deleting an individual tender retains a hidden record to prevent reimport; the admin's **Clear all tenders** action permanently removes every tender, deletion marker, product, match, discovery history, feedback, and application, so lots may be imported again.

New matches appear at the top as they stream in. Customers can mark a matched tender as **Applied**; this records a request for preparation in the demo database and does not submit an application to UZEX or trigger a real preparation process. Application records are separate from likes and dislikes, survive later searches, and are removed if the associated customer or tender is deleted.

Each customer evaluation is a billable model invocation, with bounded retries for throttling. Results are saved per customer and streamed to Business Banking; a failed evaluation preserves the previous results. Loan-application invitations are calculated locally: the tender must be in UZS, a pre-approved limit must cover at least 80% of the tender amount, and both current overdue amount and maximum overdue days in the last 12 months must be zero. Suggested amounts do not exceed the limit and are not loan approvals. Loan requests are previews only.

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

## Configuration

The default values are defined in `backend/TenderAI.Api/appsettings.json`. Docker Compose accepts optional `BEDROCK_REGION` and `BEDROCK_MODEL_ID` environment variables; the deployment workflow reads the same-named GitHub Actions repository variables. Set both variables if selecting a different source Region or inference profile. Keep AWS credentials out of configuration: the AWS SDK uses the EC2 instance role/default credential chain.

The AWS Console notice requesting use-case details is for Anthropic model onboarding, not Amazon Nova. Nova Micro does not require that Anthropic-specific form or an uploaded use-case document.

Bedrock model invocation is billable. Actual availability and account-level restrictions should be checked in the AWS account before deployment.

## References

- [Nova Micro model card and regional availability](https://docs.aws.amazon.com/bedrock/latest/userguide/model-card-amazon-nova-micro.html)
- [Prerequisites for inference profiles and IAM policy requirements](https://docs.aws.amazon.com/bedrock/latest/userguide/inference-profiles-prereq.html)
