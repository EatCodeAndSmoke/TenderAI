# Amazon Bedrock tender summaries and matching

The API defaults to Amazon Nova Micro's EU geographic inference profile:

- Region: `eu-central-1` (Frankfurt)
- Model ID: `eu.amazon.nova-micro-v1:0`

Nova Micro has Geo inference, but not in-Region inference, in Frankfurt. The EU profile routes only within the EU geography. Its Nova Micro destination Regions are Frankfurt (`eu-central-1`), Stockholm (`eu-north-1`), Ireland (`eu-west-1`), and Paris (`eu-west-3`). The profile ID is deliberately separate from the underlying foundation-model ID: application requests use `eu.amazon.nova-micro-v1:0`, while IAM permissions must include the `amazon.nova-micro-v1:0` foundation model in each destination Region.

## Tender matching

Selecting **Start matching** evaluates each database customer once against the list of currently open UZEX tenders. The model receives the customer ID, OKED code/activity, location, up to 30 incoming-payment descriptions from the preceding 12 months, and each open tender's ID, location, description, and product details. Customer names, TINs, balances, pre-approved limits, and overdue data are not sent to Bedrock. A match is saved only when both the OKED activity and payment evidence support the tender product; location is a preference, not a requirement, and incomplete or ambiguous evidence fails closed.

Each customer evaluation is one billable model invocation, with at most two customer evaluations in flight and bounded retries for throttling. Results are saved per customer and streamed to the admin UI as each evaluation completes; a failed customer does not discard other completed results. Loan-application invitations are calculated locally: a pre-approved limit must cover at least 80% of the tender amount, and both current overdue amount and maximum overdue days in the last 12 months must be zero. Suggested amounts do not exceed the limit and are not loan approvals.

New databases do not receive fictional customer or notification seed data automatically. The Customers page's **Generate Random Customers** button creates and saves three fictional demo profiles from currently open tenders that have products and a supported activity: two profiles have product-specific incoming-payment evidence as positive-match candidates, and one is intentionally non-matching. Load open UZEX tenders first. Matching still evaluates the saved customer and tender records with Bedrock. Startup removes the exact legacy sample profiles and old notifications that have no matching explanation.

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
